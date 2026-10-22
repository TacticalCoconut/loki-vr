using System;
using System.Collections.Generic;
using Loki.Horde;
using Loki.Horde.Net;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Loki.Net.Stdb
{
    /// <summary>
    /// <see cref="IHordeChannel"/> over the Loki Online module (port wave I, ihorde): on SpacetimeDB the module is Horde's host
    /// (server/src/horde: the engine ported from Horde/Core, its service). Nothing of the wire is here: client wave 8's
    /// <see cref="HordeClient"/> (the messages, the snapshots, my own movement's prediction) is the channel, exactly as over
    /// Loki Host's relay (<see cref="HordeRelayChannel"/>), and this class is only its byte transport:
    ///   up    every frame HordeClient writes (u16 length + payload: H_HELLO, H_START, H_ANSWER, H_INPUT, H_CHOOSE, H_LEAVE)
    ///         is the argument of the module's <c>horde</c> reducer, as it is;
    ///   down  a <c>horde_out</c> row (an event table of which each client receives only its own rows, like <c>packet_out</c>)
    ///         holds what one reducer call sent me: one or more payloads, framed the same way, in order (the rows of one
    ///         transaction reach a client in no order it could rely on; inside a row the order is the module's).
    /// The module knows from its own world who the caller is (account, character, name): nothing a client claims decides it.
    /// It answers H_HELLO only once my character is in its world, so the hello is said again until the first row arrives.
    ///
    /// The transport rides on <see cref="StdbSession"/>'s connection (its own subscription; it never closes the connection)
    /// and follows it: a connection that went, or a new one, is a link lost and a link made for HordeClient. Rows arrive
    /// inside the SDK's FrameTick (the main thread) and are queued by HordeClient until its own Poll.
    /// <see cref="Poll"/> must be called once a frame (LokiGame.UpdateW8Horde).
    /// </summary>
    public sealed class StdbHordeChannel : IDisposable
    {
        /// <summary>The channel the game talks to.</summary>
        public readonly HordeClient Client;
        public string Describe => "Loki Online module " + (_s.ServerKey ?? "");
        /// <summary>While the module has not answered (no character in the world yet on its side), H_HELLO is said again this often.</summary>
        public const float HelloEverySeconds = 2f;
        /// <summary>Reducer calls made, rows and payloads received (logs and smokes).</summary>
        public int Calls, Rows, Payloads;

        private readonly StdbSession _s;
        private DbConnection _conn; // the connection my handlers are on (the session makes a new one when it connects again)
        private SubscriptionHandle _sub;
        private bool _subscribed, _linked, _answered, _disposed;
        private byte[] _hello;
        private float _nextHello;
        private byte[] _rx = new byte[4096];

        public static StdbHordeChannel Open(StdbSession session, HordeContent content, int roSex)
        {
            var ch = new StdbHordeChannel(session, content, roSex);
            Debug.Log($"[Horde] channel: the {ch.Describe}; tables {ch.Client.ContentStamp}");
            return ch;
        }

        /// <param name="roSex">The real character's sex as the server sends it (0 female, 1 male): jobs that depend on it follow it.</param>
        private StdbHordeChannel(StdbSession session, HordeContent content, int roSex)
        {
            _s = session;
            Client = new HordeClient(content, () => Time.realtimeSinceStartupAsDouble)
            {
                Sex = (byte)(roSex == 1 ? 1 : roSex == 0 ? 2 : 0),
                Log = line => Debug.Log("[Horde] " + line),
            };
            Client.Send = Send;
            Attach();
        }

        /// <summary>Hooks the session's current connection: my rows, my subscription; HordeClient is told once the rows can arrive.</summary>
        private void Attach()
        {
            var conn = _s.Conn;
            if (conn == null || !conn.IsActive) return;
            _conn = conn; _subscribed = false; _linked = false; _answered = false; _hello = null;
            conn.Db.HordeOut.OnInsert += OnRow;
            try
            {
                _sub = conn.SubscriptionBuilder()
                    .OnApplied(_ => { if (conn == _conn && !_disposed) _subscribed = true; })
                    .OnError((_, e) =>
                    {
                        // a module from before Horde has no such table: the mode says "this host has no Horde", nothing is sent
                        Debug.LogWarning("[Horde] the module's Horde subscription failed (" + e.Message + "): this server has no Horde");
                        if (conn == _conn && !_disposed && !_linked) { _linked = true; Client.OnLinked(false); }
                    })
                    .AddQuery(qb => qb.From.HordeOut())
                    .Subscribe();
            }
            catch (Exception e) { Debug.LogWarning("[Horde] cannot subscribe to the module's Horde rows: " + e.Message); }
        }

        private void Detach()
        {
            var conn = _conn;
            if (conn == null) return;
            try { conn.Db.HordeOut.OnInsert -= OnRow; } catch (Exception) { }
            try { if (_sub != null && _sub.IsActive) _sub.Unsubscribe(); } catch (Exception) { }
            _sub = null; _conn = null;
        }

        public void NotifyEnteredMap()
        {
            if (!_answered) _nextHello = 0f;
        }

        private readonly List<byte> _inputBatch = new List<byte>(32);
        private int _inputBatchCount, _zeroInputsInRow;
        private float _inputBatchFlushAt;

        /// <summary>One whole frame of HordeClient (u16 length + payload): a call of the module's reducer.</summary>
        private void Send(byte[] frame)
        {
            if (frame == null || frame.Length < 3) return;
            if (frame[2] == HordeWire.HHello) { _hello = frame; _answered = false; _nextHello = Time.unscaledTime + (_s.State == RoSession.Stage.InGame ? 0.25f : HelloEverySeconds); }
            if (frame[2] == HordeWire.HInput && frame.Length >= 7)
            {
                sbyte dx = (sbyte)frame[5], dy = (sbyte)frame[6];
                if (dx == 0 && dy == 0)
                {
                    _zeroInputsInRow++;
                    if (_zeroInputsInRow > 8) return; // already standing completely still on the server
                }
                else _zeroInputsInRow = 0;

                if (_inputBatchCount == 0) _inputBatchFlushAt = Time.unscaledTime + 0.045f;
                _inputBatch.AddRange(frame);
                _inputBatchCount++;
                if (_inputBatchCount >= 2 || Time.unscaledTime >= _inputBatchFlushAt) FlushInputBatch();
                return;
            }
            FlushInputBatch();
            Call(frame);
        }

        private void FlushInputBatch()
        {
            if (_inputBatchCount == 0) return;
            var batch = new List<byte>(_inputBatch);
            _inputBatch.Clear();
            _inputBatchCount = 0;
            var conn = _conn;
            if (conn == null || !conn.IsActive) return;
            try { conn.Reducers.Horde(batch); Calls++; }
            catch (Exception e) { Debug.LogWarning("[Horde] horde call: " + e.Message); }
        }

        private void Call(byte[] frame)
        {
            var conn = _conn;
            if (conn == null || !conn.IsActive) return;
            try { conn.Reducers.Horde(new List<byte>(frame)); Calls++; }
            catch (Exception e) { Debug.LogWarning("[Horde] horde call: " + e.Message); }
        }

        /// <summary>What one reducer call of the module sent me (inside the SDK's FrameTick, the main thread): framed payloads, in order.</summary>
        private void OnRow(EventContext ctx, HordeOut row)
        {
            var d = row.Data;
            int n = d != null ? d.Count : 0;
            if (n < 3 || _disposed) return;
            if (_rx.Length < n) _rx = new byte[Math.Max(n, _rx.Length * 2)];
            d.CopyTo(_rx);
            Rows++; _answered = true;
            int o = 0;
            while (o + 2 <= n)
            {
                int len = _rx[o] | _rx[o + 1] << 8;
                o += 2;
                if (len < 1 || o + len > n) break;
                Payloads++;
                Client.OnFrame(_rx, o, len); // (copied and queued until HordeClient.Poll)
                o += len;
            }
        }

        /// <summary>Once a frame: follows the session's connection; says H_HELLO again while the module has not answered.</summary>
        public void Poll()
        {
            if (_disposed) return;
            var now = _s.Conn;
            if (_conn != null && (now != _conn || !_conn.IsActive))
            {
                bool was = _linked;
                Detach();
                _linked = false; _subscribed = false; _answered = false; _hello = null;
                _inputBatch.Clear(); _inputBatchCount = 0; _zeroInputsInRow = 0;
                if (was) { Debug.LogWarning($"[Horde] the {Describe} link dropped"); Client.OnUnlinked(); }
            }
            if (_conn == null && now != null && now.IsActive) Attach();
            if (_conn == null) return;
            if (_inputBatchCount > 0 && Time.unscaledTime >= _inputBatchFlushAt) FlushInputBatch();
            if (_subscribed && !_linked) { _linked = true; Client.OnLinked(true); } // (its next Poll says H_HELLO through Send)
            if (_linked && !_answered && _hello != null && Time.unscaledTime >= _nextHello)
            {
                // (the module answers once my character is in its world: right after the map login it may not be yet)
                _nextHello = Time.unscaledTime + (_s.State == RoSession.Stage.InGame ? 0.25f : HelloEverySeconds);
                Call(_hello);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
            if (_linked) Client.OnUnlinked();
            _linked = false;
        }
    }
}
