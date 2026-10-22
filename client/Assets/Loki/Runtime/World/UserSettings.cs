using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// What a player sets up inside the game and keeps between runs: the servers they added on the login screen and the
    /// Ragnarok Online folder they picked on the first run. One JSON file per Windows user, never in the game's own folder
    /// (StreamingAssets ships with the build and may be read-only):
    ///   &lt;Application.persistentDataPath&gt;\loki-user.json  (C:\Users\&lt;you&gt;\AppData\LocalLow\Project Loki\Loki)
    /// Tests give each run its own folder with <c>-lokiUserDir &lt;folder&gt;</c>, so a smoke never touches the player's file.
    /// Nothing here is rAthena-specific: server entries are the login screen's (rAthena or Loki Online alike).
    /// </summary>
    public static class UserSettings
    {
        [Serializable] public sealed class Server
        {
            public string name = ""; public string host = ""; public int port = 6900;
            /// <summary>"rathena" or "spacetimedb" (Loki Online).</summary>
            public string kind = "rathena";
            /// <summary>Loki Online only: the SpacetimeDB database, and "ws" / "wss".</summary>
            public string database = "loki"; public string scheme = "";
            /// <summary>The headset pose relay's port (w3presence); 0 = the login port + 3. Kept as it was when the entry is edited.</summary>
            public int presencePort = 0;
            /// <summary>Client wave 4: the player's edit of a server that comes with Loki (loki.json) — that entry's key
            /// ("kind|name|host:port"); it then shows this one instead. "" for a server the player added.</summary>
            public string replaces = "";
        }

        /// <summary>Client wave 4 (w4vr): the Settings window (VR/Comfort.cs reads and writes these; before wave 4 they lived in
        /// PlayerPrefs, which Comfort imports once, read-only, when <see cref="saved"/> is false). Nothing here is rAthena-specific.</summary>
        [Serializable] public sealed class Settings
        {
            /// <summary>False until the settings were written once (then the PlayerPrefs import never runs again).</summary>
            public bool saved;
            // Comfort
            public bool snapTurn; public int snapAngle = 45; public float turnSpeed = 90f;
            public bool vignette = true; public float vignetteStrength = 0.65f; public bool snapKnockback;
            /// <summary>Client wave 19 (w19world), point 48: the Follow view's character edge glow (VR/Comfort.cs EdgeGlow; -1: the default).</summary>
            public bool edgeGlow = true; public float edgeGlowStrength = -1f, edgeGlowSensitivity = -1f;
            public bool seated; public float tableHeight = 0.95f; public bool tutorialDone;
            /// <summary>Client wave 5 (w5weather): the rain's lightning flashes over the view (VR/Comfort.cs LightningFlashes), off by default.</summary>
            public bool lightningFlashes;
            // Controls
            public bool leftHanded; public bool haptics = true; public float hapticStrength = 0.6f;
            public bool uiRumble = true; public bool heartbeat = true; public bool quickBar = true;
            /// <summary>0: the laser leaves from the controller's pointing (aim) pose; 1: along the controller body (grip pose).</summary>
            public int laserPose;
            /// <summary>The buttons floating over the walking wrist: 0 automatic (controllers without A / B, hands), 1 always, 2 never.</summary>
            public int buttonStrip;
            // Audio
            public float masterVolume = 1f, music = 0.45f, effects = 0.9f;
            // Graphics
            public int effectsLevel; public float nameScale = 1f;
            /// <summary>XRSettings.eyeTextureResolutionScale (1 = the headset's own resolution).</summary>
            public float renderScale = 1f;
            /// <summary>QualitySettings.antiAliasing: -1 keeps the build's, 0 off, 2 / 4 / 8 samples.</summary>
            public int msaa = -1;
            /// <summary>0 off, 1 near (25 m), 2 far (60 m, the build's default: Editor/LokiSetup.cs).</summary>
            public int shadows = 2;
            // Headsets (w3presence's floating headsets)
            public bool showHeadsets = true, shareHeadset = true;
            /// <summary>Client wave 18 (w18share), the design's point 33: my casting menus are shown to friends whose headsets show mine (on unless
            /// chosen off), to everyone on my map, or (menusEveryone off) only to those of the three relations switched on: friends, family
            /// (spouse, parents, child), guild. VR/Comfort.cs MenusShared / MenusEveryone / MenusFriends / MenusFamily / MenusGuild.</summary>
            public bool menusShared = true, menusEveryone = true, menusFriends = true, menusFamily = true, menusGuild = true;
            /// <summary>Client wave 20 (w20pen), the design's point 57: who sees my air pen drawings (drawSeen*: everyone, or the relations switched on;
            /// friends only by default) and whose drawings I see (drawSee*: the same; friends only by default). VR/Comfort.cs DrawSeen* / DrawSee*.
            /// The account's (not in the per-PC list).</summary>
            public bool drawSeenEveryone, drawSeenFriends = true, drawSeenFamily, drawSeenGuild;
            public bool drawSeeEveryone, drawSeeFriends = true, drawSeeFamily, drawSeeGuild;
            /// <summary>Client wave 5 (w5fxown): Loki's own skill effects (a design for the skills without a sourced one: Fx/OwnSkillFx.cs), on unless chosen off.</summary>
            public bool ownSkillEffects = true;
            /// <summary>Client wave 5 (w5looks): skills' camera quakes and Hallucination's upside-down view (Comfort page), both off unless chosen.</summary>
            public bool cameraShake, hallucinationFlip;
            /// <summary>Client wave 9 (h9feel): monsters' and other players' sounds: 0 off, 1 low (a few a second, near you: the default), 2 normal (Audio/LokiAudio.cs MonsterSounds).</summary>
            public int monsterSounds = 1;
            /// <summary>The quick bar's shown hotkey tab per character (rAthena keeps each tab's row shift, not which tab is shown).</summary>
            public QuickTab[] quickTabs = new QuickTab[0];
            /// <summary>Client wave 4b (w4lang): the language the player chose ("en" or "pt-BR"); "" = not chosen yet, the game
            /// follows Windows' UI language (Lang.cs).</summary>
            public string language = "";
            /// <summary>Client wave 5 (w5ui): Frost Joke / Scream say a line of the game's joke lists in chat, as the official client and
            /// roBrowserLegacy do (Game/JokeLines.cs); on by default, as official.</summary>
            public bool jokeLines = true;
            /// <summary>Client wave 5 (w5ai): the custom homunculus / mercenary AI (/hoai, /merai: the player's AI\USER_AI\AI.lua /
            /// AI_M.lua) instead of the default one. Kept per user (roBrowserLegacy keeps it for the session only; Loki's choice).</summary>
            public bool aiHomCustom, aiMercCustom;
            /// <summary>Client wave 6 (w6voice): voice between friends over the headset relay (Voice/VoiceSettings.cs reads and writes
            /// these; the Settings window's "Voice" page). Share my voice (an always-open microphone behind a noise gate: on unless
            /// chosen off, the design's choice of 2026-10-04), hear friends' voices, hear them on other maps too.</summary>
            public bool voiceShare = true, voiceHear = true, voiceHearOtherMaps = true;
            /// <summary>The microphone as Unity names it ("" = Windows' default recording device), its gain, the noise gate's step
            /// (0 off: everything is sent; 1 low, 2 medium, 3 high: Voice/VoiceGate.cs), and the volume of friends' voices.</summary>
            public string voiceMic = ""; public float voiceMicGain = 1f; public int voiceGate = 2; public float voiceVolume = 1f;
            /// <summary>The first-time voice notice was answered (it is shown once, before the microphone is ever opened).</summary>
            public bool voiceNoticeDone;
            /// <summary>Per speaker: volume and mute, by RO account id (with the name last heard, for the Voice page's list).</summary>
            public VoicePeer[] voicePeers = new VoicePeer[0];
            /// <summary>Client wave 8 (w8game): other players' Horde invitations are shown (the mode's panel switches it).</summary>
            public bool hordeInvites = true;
            /// <summary>Client wave 9 (h9flow): the Settings window's "Horde" page. How the table follows the run character (h9move's follow
            /// code reads it: 0 smooth, 1 tight, 2 fixed table), and the Horde HUD's size (a factor: 0.8, 1, 1.25, 1.5).</summary>
            public int hordeFollow; public float hordeHudScale = 1f;
            /// <summary>Client wave 17 (h17read), the readability proposal's F1: Settings → Horde "Combat effects": 0 Normal (the default), 1 Large
            /// (the marks of shots, warnings and hits 1.5 × bigger: Horde/Client/HordeReadability.cs Size).</summary>
            public int hordeFxSize;
            /// <summary>Client wave 14 (w14kit), point 24: the Character presets' record per character (what the active preset gave, what
            /// the character was before the first one): Game/CharPresets.cs.</summary>
            public CharPreset[] charPresets = new CharPreset[0];
            /// <summary>Client wave 10 (w10move): Settings → Comfort → "Movement". true (the default) = Smooth: units are drawn on smoothed
            /// paths and the walking stick steers in any direction; false = Classic: cell to cell and a cell-target stick, as before
            /// (Sprites/Unit.Smooth.cs, Game/StickSteer.cs). The server and the rules are the same either way. Free movement (wfree) stops
            /// anywhere in Smooth; the client wave 26 merge (design note) shows the choice as "Free" (the field and its values kept).</summary>
            public bool smoothMove = true;
            /// <summary>Client wave 11 (w11menu): where "choose by looking" aims (Settings → Comfort; VR/Comfort.cs GazeSide / GazeUp /
            /// GazeDot; the rig's one gaze ray: VR/DioramaRig.Ui.cs). gazeSide moves the ray's START between the eyes: -1 the left
            /// eye, 0 the middle (as before), +1 the right eye; gazeUp turns it up (+) or down (-) in degrees; gazeDot shows the dot
            /// where the ray lands all the time (false: only while that Settings page is open).</summary>
            public float gazeSide, gazeUp; public bool gazeDot;
            /// <summary>Client wave 11 (w11menu): Follow view, how many degrees more steeply than the level spot the character is seen
            /// (7 = the design's number of 2026-10-01; the turning hand moved up / down while the turn stick is held changes it:
            /// VR/ViewController.cs).</summary>
            public float followPitch = 7f;
            /// <summary>Client wave 19 (w19view41), point 55: the view the player last chose (0 Table, 1 Follow, 2 First person; -1 none yet:
            /// a new player starts in Follow). The account's, like every field not in <see cref="PerPcFields"/>.</summary>
            public int viewMode = -1;
            /// <summary>Client wave 11 (w11menu), part 2: the menu's style: 0 new (a ring of groups over the hand, boards in front of the
            /// player), 1 classic (the wrist menu); and where / how large the player left the board: a factor on its size, its
            /// distance from the eyes and its height against them, in metres (UI/MenuBoard.cs).</summary>
            public int menuStyle; public float boardScale = 1f, boardDistance = 0.65f, boardHeight = -0.10f;
            /// <summary>Client wave 11 (w11menu): how the new menu's ring is worked: false (the default) = point (the walking stick,
            /// the laser, or looking + trigger); true = twist (rolling the hand that carries the ring turns it; the icon at the top
            /// is chosen; looking does not choose on the ring): UI/MenuRing.cs.</summary>
            public bool ringTwist;
            /// <summary>Client wave 13 (w13items), item 11: B and X on a menu opened from another: 0 close (the default), 1 back (UI/MenuNav.cs).</summary>
            public int menuBack;
            /// <summary>Client wave 12 (w12cast): the casting gesture (Settings → Controls): 0 off, 1 pad (the default), 2 flick, 3 wand
            /// (LokiGame.W12Cast.cs); and whether a skill that needs no target, or a target skill with a unit already locked, is cast
            /// when it is taken (true, the default) or stays in the hand until the trigger.</summary>
            public int castGesture = 1; public bool castOnTake = true;
            /// <summary>Client wave 12 (w12cast): where the wand's holster is, as an offset from the body anchor (metres: to the right,
            /// up, ahead of the point under the head, in the body's own heading: VR/WandHolster.cs); wandPlaced false = never moved:
            /// the default place on the pointing hand's side.</summary>
            public bool wandPlaced; public float wandX = 0.22f, wandY = -0.60f, wandZ = 0.05f;
            /// <summary>Client wave 12 (w12cast): the six item buttons (grip + a face button / the stick click) per character:
            /// item ids for left lower / left upper / left stick click / right lower / right upper / right stick click; 0 = none.
            /// RETIRED in client wave 14 (w14cast; the design's answer 5: "Just remove the item button shortcut altogether"): nothing reads
            /// or writes it; kept so a file that has it keeps it.</summary>
            public ItemButtons[] itemButtons = new ItemButtons[0];
            /// <summary>Client wave 12 (w12cast): the wand's quick cast (Settings → Controls): 0 off, 1 strokes, 2 buttons, 3 both (the
            /// default; "strokes" is the two-tier wheel since the design's sketch of 21:40); and the wand's own slots per character: the
            /// wheel's four quick slots (its inner segments: up, right, down, left) and four button slots (the lower face button,
            /// the stick click, stick up, stick down), each a skill id (0 = none) with the level it was pinned at.</summary>
            public int wandQuick = 3;
            /// <summary>Client wave 12 (w12cast): "Wand aim": false (the default) = a cast goes where the wand points; true = where the
            /// player looks, at once when the trigger is let go (LokiGame.W12Wand.cs).</summary>
            public bool wandAimLook;
            /// <summary>Item 5: "Aim spells with vision" toggle (overhead menu + Settings → Controls → Casting): when true, spells target where the player looks rather than where the controller points.</summary>
            public bool visionAimSpells;
            public WandSlots[] wandSlots = new WandSlots[0];
            /// <summary>Client wave 13 (w13resize): the casting pad's cell, metres (Settings → Controls "Pad size"; 0 in an older file:
            /// the default, VR/Comfort.cs PadCellDefault).</summary>
            public float padCell;
            /// <summary>Client wave 13, item 4 (w13ring): each rumble kind's own switch and strength (VR/Comfort.cs Haptics.Kind, by its
            /// name; a kind not in the file: on, 100 %). The older uiRumble / heartbeat stay in step with the Ui / LowHp kinds.</summary>
            public RumbleKind[] rumbleKinds = new RumbleKind[0];
            /// <summary>Client wave 13, item 9 (w13ring): the menu ring's twist (the arch): degrees of wrist roll per step (0 in an older
            /// file: the default, UI/MenuRing.cs TwistStepDefault).</summary>
            public float ringTwistStep;
            /// <summary>Client wave 13 (w13view). The wand's wheel shown in front of the eyes (true, the default) or at the wand, and
            /// where the player put the front picture: an offset in the HEAD's frame (metres: right, up, ahead) and a size factor
            /// (wheelPlaced false: the default place, UI/WandWheel.cs). The see-through minimap: shown, its zoom step (0-2), its place
            /// in the head's frame and size factor (UI/Minimap.cs). The forearm quick bar: -1 = an older file (then quickBar says
            /// on / off), 0 always on, 1 always off, 2 a button at the elbow; whether the bar is shown in that mode; the elbow
            /// button's place along the forearm (metres from the estimated elbow, + towards the hand: LokiGame.W13View.cs).</summary>
            public bool wandWheelFront = true, wheelPlaced; public float wheelX, wheelY, wheelZ, wheelScale = 1f;
            public bool minimapOn, minimapPlaced; public int minimapZoom; public float minimapX, minimapY, minimapZ, minimapScale = 1f;
            public int quickBarMode = -1; public bool elbowBarOpen = true; public float elbowOffset;
            /// <summary>Client wave 13 (w13grab), item 19: a held panel follows the hand's full rotation (true) or stays upright, turning
            /// only about the vertical (false, the default: VR/Comfort.cs PanelTiltFree).</summary>
            public bool panelTiltFree;
            /// <summary>Client wave 13 (w13look), items 23 and 24: "Look select" (true, the default: the look chooses; false: nothing is
            /// chosen by looking), and the panel kinds switched out of the look's choices by their eye (UI/LookSelect.cs keys; empty:
            /// every panel is a valid choice). The baseline never changes the list.</summary>
            public bool lookSelect = true; public string[] lookOff = new string[0];
            /// <summary>Client wave 13 (w13look), item 25: the item buttons' crosses as Reposition left them: placed, their place in the HEAD's
            /// frame (metres: right, up, ahead) and their size factor. RETIRED in client wave 14 (w14cast) with the item buttons: nothing
            /// reads or writes it; kept so a file that has it keeps it.</summary>
            public bool crossPlaced; public float crossX, crossY, crossZ, crossScale = 1f;
            /// <summary>Client wave 14 (w14cast): "Cast grip" 0 light (the default) / 1 medium / 2 firm; the wand's "Wand rows" 0 in place
            /// (the default) / 1 beside; the wand's size factor (0 in an older file: 1). VR/Comfort.cs CastGrip / WandRows / WandScale.</summary>
            public int castGrip, wandRows; public float wandScale = 1f;
            // client wave 14 (w14laser), point 20
            /// <summary>Client wave 14 (w14laser), point 20: "Laser walking" (true, the default: the pointing hand's laser + trigger on the ground
            /// walks there and the laser is drawn on the ground; false: it does not, and the laser shows only on panels and units: VR/Comfort.cs LaserWalk).</summary>
            public bool laserWalk = true;
            // ---- client wave 15 (w15grab), point 8
            /// <summary>Point 8: "Panel smoothing", 0 (off) to 1 (the most); -1 in an older file: the default (VR/Comfort.cs PanelSmoothingDefault).</summary>
            public float panelSmoothing = -1f;
            // ---- client wave 14 (w14menu)
            /// <summary>Points 2 and 3: the menu ring's twist mode: degrees of wrist per step (0 in an older file: the default, VR/Comfort.cs
            /// RingTwistStepDefault; wave 13's ringTwistStep above is no longer read), the arch (true) or the circle (false, the default: item 28),
            /// the icons still with a moving selector (true) or sliding under a fixed marker.</summary>
            public float ringTurn; public bool ringArch = false, ringStill = true;
            /// <summary>Client wave 15 (w15menu), point 1: the twist ring's direction, reversed (true, the default: playtesting found wave 14's way
            /// inverted) or wave 14's (false). An older file has none: reversed.</summary>
            public bool ringReversed = true;
            /// <summary>Point 8: the overhead menu as Reposition left it: placed, its place in the BODY's frame from the eyes (metres: right,
            /// up, ahead; overheadPlaced false: 60° up, 0.75 m: UI/OverheadMenu.cs) and its size factor.</summary>
            public bool overheadPlaced; public float overheadX, overheadY, overheadZ, overheadScale = 1f;
            // ---- client wave 18 (w18menu), point 2
            /// <summary>The twist ring's snap: on (true, the default), its strength and range (-1 in an older file: the defaults, VR/Comfort.cs
            /// RingSnapStrengthDefault / RingSnapRangeDefault). Point 6: panelSmoothing above now goes to 2 (200 %).</summary>
            public bool ringSnap = true; public float ringSnapStrength = -1f, ringSnapRange = -1f;
            // ---- client wave 18 (w18wand), point 14
            /// <summary>Point 14: how many slices the wand's inner wheel has (Settings → Controls → Wand "Wand slices", 2-8); 0 in an older
            /// file: the default, 4 (VR/Comfort.cs WandSlices).</summary>
            public int wandSlices;
            // ---- client wave 18 (w18flick)
            /// <summary>Point 7: "Flick follow", seconds (0, the default and an older file's: the row's middle does not follow the hand). Point 16:
            /// "Pad rows": 0 Palm (the default, and every older file's: a file of an early wave 18 build that wrote "padSlap" is not read for it),
            /// 1 Slap; "Slap strength", m/s (0 in an older file: the default, VR/Comfort.cs SlapSpeedDefault). Point 15: each hand's pad place from
            /// Reposition (VR/Comfort.cs PadOffset / PadTurn; an older file: none, the pad on the fingertip).</summary>
            public float flickFollow; public int padRows; public float slapSpeed;
            public PadPlace padLeft = new PadPlace(), padRight = new PadPlace();
            // ---- client wave 24 (w24): the login panel and the keyboard are taken by their edge and stay where they are put
            // (UI/PanelPlacing.cs: a place is kept in the head's level frame: right, up, ahead of the eyes, and a turn).
            /// <summary>The login panel's place: THIS PC's (in PerPcFields).</summary>
            public bool loginPlaced; public PadPlace loginPlace = new PadPlace();
            /// <summary>The keyboard's place on the screens before the world: THIS PC's (in PerPcFields).</summary>
            public bool kbLoginPlaced; public PadPlace kbLoginPlace = new PadPlace();
            /// <summary>The keyboard's place in the world. The account's, like every field not in <see cref="PerPcFields"/> (as the
            /// other places: the minimap's, the pads' ...).</summary>
            public bool kbPlaced; public PadPlace kbPlace = new PadPlace();
            /// <summary>Client wave 24 (w24), item 17 (design note: "The chat box should not be open by default"): the chat box as the player
            /// last left it: 0 = never chosen (it starts hidden), 1 = shown, 2 = hidden. Until then the choice lived for one launch
            /// only (UI/LokiHud.cs ChatWanted) and began shown. The account's, like every field not in <see cref="PerPcFields"/>.</summary>
            public int chatShown;
            /// <summary>Point 32: "Flick shown": true = in front of you, false (the default and an older file's) = at your hand.</summary>
            public bool flickFront;
            /// <summary>Item 43: flick grip pressure threshold (0.15..0.95, default 0.75) and hand mode (0 Dominant, 1 Off-hand, 2 Both).
            /// Item 48: whether radial skill wheels were chosen as the active casting method.</summary>
            public float flickGripThreshold = 0.75f; public int flickHandMode = 2; public bool skillWheels;
            // ---- client wave 21 (w21tools)
            /// <summary>"Casting look": false (the default and an older file's) = Themed, true = Plain: the casting tools as waves 12-20 drew
            /// them (VR/Comfort.cs CastPlain, UI/CastLook.cs). The account's, like every field not in <see cref="PerPcFields"/>.</summary>
            public bool castPlain;
            // ---- client wave 18 (w18tut), point 34
            /// <summary>The characters that had the interactive tutorial (finished or ended), "Name#charId" (LokiGame.W18Tutorial.cs): a new
            /// character's first login opens it once; Settings → Controls "Controls tutorial" shows it again for anyone.</summary>
            public string[] tutorialChars = new string[0];
            // ---- client wave 20 (w20tut), point 65: the look calibration, THIS PC's (design note: "Stored per PC"; in PerPcFields)
            /// <summary>The look calibration's correction (VR/LookCalibration.cs): measured on this PC, degrees sideways (+ right) and up / down
            /// (+ up) added to the look ray in the head's frame; how many dots it averaged; when (UTC). lookCalDone false: none (no correction).</summary>
            public bool lookCalDone; public float lookCalYaw, lookCalPitch; public int lookCalDots; public string lookCalAt = "";
            // ---- client wave 20 (w20bubbles), point 56: the Quick Use Bubbles (LokiGame.W20Bubbles.cs, UI/QuickBubble.cs)
            /// <summary>Point 56: the bubbles' LAYOUT (the account's, like every field not in <see cref="PerPcFields"/>): per bubble On / Off,
            /// its place and size, "Shown" and "Use by" (<see cref="Bubble"/>); an older file has none: the defaults (<see cref="BubblesNow"/>).</summary>
            public Bubble[] bubbles = new Bubble[0];
            /// <summary>Client wave 24 (w24), item 11 (design note: "the spheres are far too transparent ... Provide a slider that controls how
            /// transparent they are"): how solid every Quick Use Bubble is drawn, 0.2 (clearly see-through) to 1 (solid); each bubble's
            /// own factor multiplies in (Bubble.opacity). The default is LokiGame.BubbleOpacityDefault. The account's, like the
            /// bubbles' layout (every field not in <see cref="PerPcFields"/>).</summary>
            public float bubbleOpacity = 0.8f;
            /// <summary>Point 56: what each bubble holds, per CHARACTER ("Name#charId"; <see cref="BubbleSet"/>). Client-side only: no RO
            /// hotkey row (design note: "It is not meant to use any of the different hotbars that the game natively carries").</summary>
            public BubbleSet[] bubbleSets = new BubbleSet[0];
            /// <summary>Client wave 21 (w21anim), the design's point 63: the menus whose opening animation is switched OFF (their keys: UI/MenuAnim.cs;
            /// every other menu's is on, a menu added later too). The account's, like every field not in <see cref="PerPcFields"/>.</summary>
            public string[] menuAnimOff = new string[0];
            // ---- client wave 21 (w21jobui; design note, 2026-10-11): the panel theme's job variants (UI/JobTheme.cs)
            /// <summary>Settings -> Controls -> Menus "Panel theme": 0 My job, 1 Base; -1 (an older file, never chosen): the default
            /// (UI/JobTheme.cs DefaultMyJob). The account's, like every field not in <see cref="PerPcFields"/>.</summary>
            public int panelTheme = -1;
            /// <summary>Client wave 21 (w21inv), the design's point 62 (the new inventory, UI/InvPanel.cs): the old bag page instead (Settings ->
            /// Controls -> Menus "Inventory": New / Old; default New), and each section's own sort and filter by the section's place
            /// (their keys: UI/InvPanel.cs SortKey / Filter names; "" = the section's first). With the account.</summary>
            public bool invOld;
            public string[] invSort = new string[0];
            public string[] invFilter = new string[0];
            /// <summary>Client wave 21 (w21inv), design note 2026-10-11: each section's number of columns by the section's place (1-4; 0 = the
            /// default 3), and the inventory's equipment panel pinned ("The state of the 'pinning' is stored on the per-account settings
            /// memory": it pulls open by itself after the inventory's opening). With the account, as every field here that is not in
            /// PerPcFields (the saved view of wave 19's point 55, viewMode above, is kept the same way).</summary>
            public int[] invCols = new int[0];
            public bool invEquipPinned;
            /// <summary>Client wave 22 (w22tree), the design's point 64: the Skills page as the flat page (true) instead of the 3D skill tree (false,
            /// the default; an older file has none: the tree). With the account, as the menu's style.</summary>
            public bool skillTreeFlat;
            /// <summary>Client wave 22 (w22tree), the design's point 64: the skill trees one at a time (true) instead of around the player (false,
            /// the default: design note, ""Around you" is indeed meant to be the default"; an older file has none: around). With the account.</summary>
            public bool skillTreesOne;
            /// <summary>Client wave 23 (w23tree), item 24: the open skill trees do NOT muffle the other sounds (false, the default: they do).</summary>
            public bool skillTreeLoud;
            // ---- wave 28 (wcodex): the Codex (UI/CodexBook.cs, Game/CodexJournal.cs)
            /// <summary>Settings → Comfort → Effects "Codex flip-through" switched off (false, the default and an older file's: the pages riffle
            /// to a chosen page). With the account.</summary>
            public bool codexFlipsOff;
            /// <summary>Settings → Comfort → Effects "Codex book": Large (true) instead of Hand-held (false, the default and an older file's:
            /// design note, 2026-10-15, "an actual book-sized book that I grab with my hands. This should be the default version"). With the account.</summary>
            public bool codexLarge;
            /// <summary>Settings → Comfort → Effects "Codex scan" switched off (false, the default and an older file's: looking at a creature
            /// fills the scan ring; design note, 2026-10-16: "add a scan on/off toggle"). Off: no ring and nothing written by looking; a kill and an
            /// NPC's line still write. With the account.</summary>
            public bool codexScanOff;
            /// <summary>The account's Codex: what it has written down (Game/CodexJournal.cs). With the account (design note: per account).</summary>
            public Loki.Game.CodexSave codex = new Loki.Game.CodexSave();
            /// <summary>Item 6: persistent per-menu position memory relative to the player's vision.</summary>
            public MenuPlace[] menuPlaces = new MenuPlace[0];
        }
        /// <summary>Client wave 20 (w20bubbles), point 56: one Quick Use Bubble's layout. <c>placed</c> false: its default place
        /// (LokiGame.W20Bubbles.cs BubbleDefault); else x / y / z, metres in the BODY's frame from the eyes (right, up, ahead), as the overhead
        /// menu's (VR/Comfort.cs OverheadOffset); <c>scale</c> its size factor; <c>shown</c> 0 Always, 1 When looked at (the default);
        /// <c>useBy</c> 0 Touch, 1 Trigger (the default: the design's answer "Yes").</summary>
        [Serializable] public sealed class Bubble
        {
            public bool on = true, placed; public float x, y, z, scale = 1f; public int shown = 1, useBy = 1;
            /// <summary>Client wave 24 (w24), item 11: this bubble's own opacity, a factor on the general one (bubbleOpacity; 1 = all of it).</summary>
            public float opacity = 1f;
        }
        /// <summary>Client wave 20 (w20bubbles), point 56: one character's bubble contents: per bubble a skill id or (its flag set) an item
        /// id; 0 = empty.</summary>
        [Serializable] public sealed class BubbleSet { public string character = ""; public int[] ids = new int[BubbleCount]; public bool[] item = new bool[BubbleCount]; }
        /// <summary>Client wave 20 (w20bubbles), point 56: how many bubbles there are (design note: "holds up to 4 items").</summary>
        public const int BubbleCount = 4;
        /// <summary>Client wave 18 (w18flick), point 15: a pad's place from its hand's fingertip: an offset (metres, the pad's frame at the
        /// fingertip: right, up, away from the eyes) and a turn (a quaternion; identity: facing the eyes).</summary>
        [Serializable] public sealed class PadPlace { public float x, y, z, qx, qy, qz, qw = 1f; }
        /// <summary>Item 6: a menu window's saved place and scale relative to the player's vision.</summary>
        [Serializable] public sealed class MenuPlace { public string key = ""; public PadPlace place = new PadPlace(); public float scale = 1f; }
        [Serializable] public sealed class RumbleKind { public string kind = ""; public bool on = true; public float strength = 1f; }
        [Serializable] public sealed class WandSlots
        {
            public string character = ""; public int[] quick = new int[MaxWandSlices], quickLv = new int[MaxWandSlices], buttons = new int[4], buttonsLv = new int[4];
            /// <summary>Client wave 18 (w18wand), points 13 and 14: the inner wheel's slots grow to <see cref="MaxWandSlices"/> (an older file's
            /// four are kept as slots 1-4), and each wheel or button slot may hold an ITEM (its flag set: <c>quick</c> / <c>buttons</c> then hold
            /// the item's id) as well as a skill (an older file has no flags: all skills, as they were).</summary>
            public bool[] quickItem = new bool[MaxWandSlices], buttonsItem = new bool[4];
        }
        /// <summary>Client wave 18 (w18wand), point 14: the most slices the wand's inner wheel may have.</summary>
        public const int MaxWandSlices = 8;
        [Serializable] public sealed class ItemButtons { public string character = ""; public int[] ids = new int[6]; }
        [Serializable] public sealed class VoicePeer { public uint aid; public string name = ""; public float volume = 1f; public bool muted; }
        [Serializable] public sealed class QuickTab { public string character = ""; public int tab; }
        /// <summary>Client wave 14 (w14kit), point 24: one character's Character presets record. <c>character</c> is the name and char id
        /// ("Name#123"); <c>preset</c> the active preset's key ("4013:katar"; "" while one is being set up); <c>origJob</c> / <c>origBase</c> /
        /// <c>origJobLevel</c> the character's job and levels before its first preset (No preset puts them back) and <c>origBar</c> its quick
        /// bar 1 then (36 slots × is skill, id, count); <c>items</c> what the preset gave (triples: item id, amount given, amount owned before):
        /// switching presets or No preset takes back min(given, owned now − owned before) of each, never more.</summary>
        [Serializable] public sealed class CharPreset
        {
            public string character = "", preset = "";
            public int origJob = -1, origBase, origJobLevel;
            public int[] origBar = new int[0], items = new int[0];
            // client wave 18 (w18gear), point 3: the character's skill points before its first preset (spent + free as rAthena counts them,
            // pc_calc_skillpoint + skill_point; -1 unknown) and its job-change levels then (rAthena's @stats "JobChangeLvl" 2nd / 3rd / 4th,
            // atcommand.cpp:9883-9885): No preset gives both back (LokiGame.W14Kit.cs)
            public int origSkill = -1;
            public int[] origChange = new int[0];
        }

        [Serializable] public sealed class Data
        {
            public int version = 1;
            /// <summary>The Ragnarok Online folder picked in game (used when loki.json's clientRoot has no game data).</summary>
            public string dataFolder = "";
            public Server[] servers = new Server[0];
            /// <summary>Client wave 4: servers that come with Loki (loki.json) the player hid, by key ("kind|name|host:port").</summary>
            public string[] hiddenServers = new string[0];
            /// <summary>Client wave 4 (w4vr): the Settings window's choices.</summary>
            public Settings settings = new Settings();
            /// <summary>Client wave 18 (w18acct): which account <see cref="settings"/> mirrors and when the player last changed them (this
            /// PC's own bookkeeping: never sent).</summary>
            public AccountSync accountSync = new AccountSync();
        }

        // ---- client wave 18 (w18acct), the design's point 1: every setting and Reposition place per ACCOUNT (the host keeps them), but
        // Audio and Graphics per PC ("The only setting that should not be saved on an account basis are the audio and graphics settings")
        /// <summary>This PC's record of the account its settings mirror (World/AccountSettingsSync.cs; World/AccountSyncRules.cs):
        /// the server ("host:port" of its login server) and the account id, when the player last changed the account's part here (ms
        /// since 1970 UTC; 0 never since wave 18), whether the host took that change, and the hash of that part as last saved.</summary>
        [Serializable] public sealed class AccountSync { public string server = ""; public uint aid; public long savedAt; public bool pushed; public string hash = ""; }

        /// <summary>
        /// The fields of <see cref="Settings"/> that stay on THIS PC (everything else goes with the account, new fields included: there
        /// is no list of those to keep). design note, 2026-10-09 (answering w18acct's flag): "the graphics and audio settings are the only ones
        /// stored on a per-PC basis, because they are hardware dependent. Different configurations of GPU and CPU, as well as audio
        /// output devices need different settings": so EVERY sound level, device and output setting is this PC's:
        ///   Audio    Settings → Audio's three volumes; on the Voice page the microphone, its gain, the noise gate (set for this
        ///            microphone and room), friends' voice volume and "Monster sounds" (how many monster / player sounds play); and each
        ///            friend's own volume (<see cref="VoicePeer.volume"/>, handled in <see cref="AccountJson"/> / <see cref="ApplyAccountJson"/>).
        ///   Graphics every row of Settings → Graphics (render scale, anti-aliasing, shadows, skill effects, Loki's skill effects, name
        ///            tags; "Costume looks" is the server's).
        ///   Look     client wave 20 (w20tut), point 65: the look calibration's correction (design note: "Stored per PC": it depends on the headset
        ///            and how it sits); the Look aim sliders stay the account's.
        ///   and <c>saved</c>, this file's own flag.
        /// The Voice page's choices that are no sound level (share my voice, friends' voices on / off, friends on other maps, the first-time
        /// notice answered) and a friend's mute go with the account. The full list, field by field: design notes
        /// </summary>
        public static readonly string[] PerPcFields =
        {
            "masterVolume", "music", "effects", "voiceMic", "voiceMicGain", "voiceGate", "voiceVolume", "monsterSounds", // Audio
            "renderScale", "msaa", "shadows", "effectsLevel", "ownSkillEffects", "nameScale",                                // Graphics
            "saved",                                                                                                          // this file
            "lookCalDone", "lookCalYaw", "lookCalPitch", "lookCalDots", "lookCalAt",                                          // client wave 20 (w20tut), point 65: the look calibration (design note: "Stored per PC")
            "loginPlaced", "loginPlace", "kbLoginPlaced", "kbLoginPlace",                                                     // client wave 24 (w24): the login panel's place and the keyboard's before the world
        };
        private static System.Reflection.FieldInfo[] _perPc;
        /// <summary>The <see cref="PerPcFields"/> as fields (a name that is not a field of Settings is left out and named in <see cref="PerPcMissing"/>).</summary>
        public static System.Reflection.FieldInfo[] PerPcInfos
        {
            get
            {
                if (_perPc != null) return _perPc;
                var l = new List<System.Reflection.FieldInfo>(); var miss = new List<string>();
                foreach (var n in PerPcFields)
                {
                    var f = typeof(Settings).GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (f != null) l.Add(f); else miss.Add(n);
                }
                PerPcMissing = miss.ToArray();
                if (miss.Count > 0) Debug.LogError("[User] per-PC settings that are no field of Settings: " + string.Join(", ", miss));
                return _perPc = l.ToArray();
            }
        }
        public static string[] PerPcMissing { get; private set; } = new string[0];

        /// <summary>The account's part of the settings as JSON: every field of <see cref="Settings"/>, the per-PC ones at their defaults
        /// (so neither what they are here nor a change of them is the account's). Friends' voices: only the muted ones go, each at the
        /// default volume (a friend's volume is this PC's; a mute is the account's).</summary>
        public static string AccountJson()
        {
            var copy = JsonUtility.FromJson<Settings>(JsonUtility.ToJson(Prefs));
            var fresh = new Settings();
            foreach (var f in PerPcInfos) f.SetValue(copy, f.GetValue(fresh));
            var muted = new List<VoicePeer>();
            foreach (var p in copy.voicePeers ?? new VoicePeer[0]) if (p != null && p.muted) { p.volume = new VoicePeer().volume; muted.Add(p); }
            copy.voicePeers = muted.ToArray();
            return JsonUtility.ToJson(copy);
        }

        /// <summary>The account's settings into this PC's (in place: what holds the object keeps it): every field the JSON has, but this
        /// PC's per-PC fields as they were; a field the JSON lacks (a save of an older Loki) keeps this PC's value. Not saved: the
        /// caller saves. False when the JSON is not settings (nothing is changed then).</summary>
        public static bool ApplyAccountJson(string json)
        {
            try { if (string.IsNullOrEmpty(json) || JsonUtility.FromJson<Settings>(json) == null) return false; }
            catch (Exception e) { Debug.LogWarning("[User] the account's settings could not be read: " + e.Message); return false; }
            var s = Prefs; var infos = PerPcInfos; var keep = new object[infos.Length];
            for (int i = 0; i < infos.Length; i++) keep[i] = infos[i].GetValue(s);
            // this PC's volume of each friend (and the name it last heard), as VALUES: FromJsonOverwrite fills the array's existing
            // objects in place (smoke w18acct H, dev 7: a reference read back the account's volume)
            var vols = new Dictionary<uint, (float volume, string name)>();
            foreach (var p in s.voicePeers ?? new VoicePeer[0]) if (p != null) vols[p.aid] = (p.volume, p.name);
            if (s.codex != null && !string.IsNullOrEmpty(s.codex.acct)) Loki.Game.CodexJournal.KeepInArchive(s.codex);
            JsonUtility.FromJsonOverwrite(json, s);
            for (int i = 0; i < infos.Length; i++) infos[i].SetValue(s, keep[i]);
            // friends: the account's mutes, this PC's volumes (a friend this PC turned up or down and the account does not mute keeps
            // that volume, not muted)
            var peers = new List<VoicePeer>(); var seen = new HashSet<uint>();
            foreach (var p in s.voicePeers ?? new VoicePeer[0])
            {
                if (p == null || !seen.Add(p.aid)) continue;
                p.volume = vols.TryGetValue(p.aid, out var mine) ? mine.volume : new VoicePeer().volume;
                peers.Add(p);
            }
            foreach (var kv in vols)
                if (!seen.Contains(kv.Key) && Mathf.Abs(kv.Value.volume - new VoicePeer().volume) > 1e-4f)
                    peers.Add(new VoicePeer { aid = kv.Key, name = kv.Value.name ?? "", volume = kv.Value.volume, muted = false });
            s.voicePeers = peers.ToArray();
            s.saved = true;
            // (JsonUtility writes every array and string, empty ones as [] / "": the ones it read are never null; the getter's checks
            // would run again only at the next Reload)
            return true;
        }

        /// <summary>The account record (never null).</summary>
        public static AccountSync Account => Current.accountSync;
        /// <summary>The hash of the account's part as it is now.</summary>
        public static string AccountHash() => AccountSyncRules.Hash(AccountJson());
        /// <summary>Saves that changed the account's part (smoke, log).</summary>
        public static int AccountChanges { get; private set; }
        /// <summary>Every save: when the account's part is not what was last saved, the record says it changed NOW and that the host
        /// has not taken it (World/AccountSettingsSync.cs sends it). A change of a per-PC field alone changes nothing here.</summary>
        private static void StampAccount()
        {
            try
            {
                var a = Account; string h = AccountHash();
                if (h == a.hash) return;
                a.hash = h; a.savedAt = AccountSyncRules.NowMs(); a.pushed = false; AccountChanges++;
            }
            catch (Exception e) { Debug.LogWarning("[User] the account's part: " + e.Message); }
        }
        // ---- (end of client wave 18, w18acct)

        private static Data _data; private static string _dir;
        /// <summary>Why the file could not be read or written (null when fine).</summary>
        public static string Error { get; private set; }

        /// <summary>The folder of the file: <c>-lokiUserDir</c>, or Unity's per-user data folder.</summary>
        public static string Dir
        {
            get
            {
                if (_dir != null) return _dir;
                var a = Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-lokiUserDir" && !string.IsNullOrWhiteSpace(a[i + 1])) _dir = a[i + 1];
                if (_dir == null) _dir = Application.persistentDataPath;
                return _dir;
            }
        }
        public static string FilePath => Path.Combine(Dir, "loki-user.json");

        public static Data Current
        {
            get
            {
                if (_data != null) return _data;
                _data = new Data();
                try
                {
                    if (File.Exists(FilePath)) _data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) ?? new Data();
                }
                catch (Exception e) { Error = $"{FilePath} could not be read: {e.Message}"; Debug.LogWarning("[User] " + Error); _data = new Data(); }
                if (_data.accountSync == null) _data.accountSync = new AccountSync(); // client wave 18 (w18acct)
                if (_data.accountSync.server == null) _data.accountSync.server = ""; if (_data.accountSync.hash == null) _data.accountSync.hash = "";
                if (_data.servers == null) _data.servers = new Server[0];
                if (_data.hiddenServers == null) _data.hiddenServers = new string[0];
                foreach (var sv in _data.servers) if (sv != null && sv.replaces == null) sv.replaces = "";
                if (_data.dataFolder == null) _data.dataFolder = "";
                if (_data.settings == null) _data.settings = new Settings();
                if (_data.settings.quickTabs == null) _data.settings.quickTabs = new QuickTab[0];
                if (_data.settings.voicePeers == null) _data.settings.voicePeers = new VoicePeer[0]; // w6voice
                if (_data.settings.voiceMic == null) _data.settings.voiceMic = "";
                if (_data.settings.itemButtons == null) _data.settings.itemButtons = new ItemButtons[0]; // client wave 12 (w12cast)
                if (_data.settings.wandSlots == null) _data.settings.wandSlots = new WandSlots[0];
                if (_data.settings.rumbleKinds == null) _data.settings.rumbleKinds = new RumbleKind[0]; // client wave 13, item 4 (w13ring)
                if (_data.settings.lookOff == null) _data.settings.lookOff = new string[0]; // client wave 13 (w13look), item 24
                if (_data.settings.lookCalAt == null) _data.settings.lookCalAt = ""; // client wave 20 (w20tut), point 65
                if (_data.settings.bubbles == null) _data.settings.bubbles = new Bubble[0]; // client wave 20 (w20bubbles), point 56
                if (_data.settings.bubbleSets == null) _data.settings.bubbleSets = new BubbleSet[0];
                if (_data.settings.menuAnimOff == null) _data.settings.menuAnimOff = new string[0]; // client wave 21 (w21anim), point 63
                if (_data.settings.invSort == null) _data.settings.invSort = new string[0]; // client wave 21 (w21inv), point 62
                if (_data.settings.invFilter == null) _data.settings.invFilter = new string[0];
                if (_data.settings.invCols == null) _data.settings.invCols = new int[0];
                if (_data.settings.codex == null) _data.settings.codex = new Loki.Game.CodexSave(); // wave 28 (wcodex)
                if (_data.settings.codex.recent == null) _data.settings.codex.recent = new string[0];
                if (_data.settings.menuPlaces == null) _data.settings.menuPlaces = new MenuPlace[0];
                return _data;
            }
        }

        public static List<Server> Servers => new List<Server>(Current.servers);

        /// <summary>Writes the file (a temporary file first, then moved over the old one, so a crash never leaves half a file).</summary>
        public static bool Save()
        {
            StampAccount(); // client wave 18 (w18acct)
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Current, true));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
                Error = null;
                Debug.Log($"[User] saved {FilePath}: {Current.servers.Length} server(s), {Current.hiddenServers.Length} hidden, data folder '{Current.dataFolder}'");
                return true;
            }
            catch (Exception e) { Error = $"{FilePath} could not be written: {e.Message}"; Debug.LogWarning("[User] " + Error); return false; }
        }

        public static void SetServers(List<Server> list) { Current.servers = list != null ? list.ToArray() : new Server[0]; }
        public static List<string> HiddenServers => new List<string>(Current.hiddenServers);
        public static void SetHiddenServers(List<string> keys) { Current.hiddenServers = keys != null ? keys.ToArray() : new string[0]; }
        /// <summary>Client wave 4 (w4vr): the Settings window's block (never null).</summary>
        public static Settings Prefs => Current.settings;
        /// <summary>The quick bar tab last shown for this character (0 when never switched).</summary>
        public static int QuickTabOf(string character)
        {
            foreach (var q in Prefs.quickTabs) if (q != null && q.character == character) return Mathf.Clamp(q.tab, 0, 1);
            return 0;
        }
        public static void SetQuickTab(string character, int tab)
        {
            var list = new List<QuickTab>(Prefs.quickTabs);
            var e = list.Find(q => q != null && q.character == character);
            if (e == null) list.Add(new QuickTab { character = character ?? "", tab = tab }); else e.tab = tab;
            Prefs.quickTabs = list.ToArray();
        }
        public static void SetDataFolder(string folder) { Current.dataFolder = folder ?? ""; }
        /// <summary>Client wave 12 (w12cast): this character's wand slots (never null, its arrays whole; the stored object: call Save after a change).</summary>
        public static WandSlots WandSlotsOf(string character)
        {
            WandSlots e = null;
            foreach (var w in Prefs.wandSlots) if (w != null && w.character == character) { e = w; break; }
            if (e == null) { e = new WandSlots { character = character ?? "" }; var list = new List<WandSlots>(Prefs.wandSlots) { e }; Prefs.wandSlots = list.ToArray(); }
            // (client wave 18, w18wand, points 13 and 14: the wheel's slots and their item flags to 8, the buttons' flags to 4)
            int m = MaxWandSlices;
            if (e.quick == null || e.quick.Length != m) e.quick = Fit(e.quick, m); if (e.quickLv == null || e.quickLv.Length != m) e.quickLv = Fit(e.quickLv, m);
            if (e.buttons == null || e.buttons.Length != 4) e.buttons = Fit(e.buttons, 4); if (e.buttonsLv == null || e.buttonsLv.Length != 4) e.buttonsLv = Fit(e.buttonsLv, 4);
            if (e.quickItem == null || e.quickItem.Length != m) e.quickItem = Fit(e.quickItem, m); if (e.buttonsItem == null || e.buttonsItem.Length != 4) e.buttonsItem = Fit(e.buttonsItem, 4);
            return e;
        }
        private static int[] Fit(int[] a, int n) { var r = new int[n]; if (a != null) for (int i = 0; i < n && i < a.Length; i++) r[i] = a[i]; return r; }
        private static bool[] Fit(bool[] a, int n) { var r = new bool[n]; if (a != null) for (int i = 0; i < n && i < a.Length; i++) r[i] = a[i]; return r; }
        /// <summary>Client wave 14 (w14kit), point 24: this character's Character presets record (null: none: it never had a preset, or No
        /// preset ended it). The stored object: call Save after a change.</summary>
        public static CharPreset CharPresetOf(string character)
        {
            foreach (var c in Prefs.charPresets ?? new CharPreset[0]) if (c != null && c.character == character) return c;
            return null;
        }
        public static void SetCharPreset(CharPreset rec)
        {
            var list = new List<CharPreset>(Prefs.charPresets ?? new CharPreset[0]);
            list.RemoveAll(c => c == null || c.character == rec.character);
            list.Add(rec);
            Prefs.charPresets = list.ToArray();
        }
        public static void ClearCharPreset(string character)
        {
            var list = new List<CharPreset>(Prefs.charPresets ?? new CharPreset[0]);
            list.RemoveAll(c => c == null || c.character == character);
            Prefs.charPresets = list.ToArray();
        }
        // ---- client wave 18 (w18tut), point 34: the characters that had the interactive tutorial ("Name#charId"; the stored list: call Save after)
        public static bool TutorialHad(string character)
        {
            foreach (var c in Prefs.tutorialChars ?? new string[0]) if (c == character) return true;
            return false;
        }
        public static void SetTutorialHad(string character)
        {
            if (string.IsNullOrEmpty(character) || TutorialHad(character)) return;
            var list = new List<string>(Prefs.tutorialChars ?? new string[0]) { character };
            Prefs.tutorialChars = list.ToArray();
        }

        // ---- client wave 20 (w20bubbles), point 56: the Quick Use Bubbles' layout (the account's) and contents (per character)
        /// <summary>The four bubbles' layout to READ (never null, always <see cref="BubbleCount"/> whole entries). A file with none (or fewer)
        /// reads the defaults for the rest WITHOUT storing them, so merely reading changes nothing of the account's part (a save would
        /// otherwise count as the player's change: World/AccountSettingsSync.cs). Read anew wherever it is used, so the account's settings
        /// arriving (<see cref="ApplyAccountJson"/>) need nothing more. To change one: <see cref="BubblesForEdit"/>.</summary>
        public static Bubble[] BubblesNow
        {
            get
            {
                var a = Prefs.bubbles;
                if (Whole(a)) return a;
                if (_bubbleFallback == null || _bubbleFallbackOf != a)
                {
                    _bubbleFallback = new Bubble[BubbleCount]; _bubbleFallbackOf = a;
                    for (int i = 0; i < BubbleCount; i++) _bubbleFallback[i] = a != null && i < a.Length && a[i] != null ? a[i] : new Bubble();
                }
                return _bubbleFallback;
            }
        }
        private static Bubble[] _bubbleFallback, _bubbleFallbackOf;
        private static bool Whole(Bubble[] a) { if (a == null || a.Length != BubbleCount) return false; foreach (var b in a) if (b == null) return false; return true; }
        /// <summary>The four bubbles' layout to CHANGE: the stored entries, made whole first (call Save after the change).</summary>
        public static Bubble[] BubblesForEdit()
        {
            if (!Whole(Prefs.bubbles)) { var r = new Bubble[BubbleCount]; var now = BubblesNow; for (int i = 0; i < BubbleCount; i++) r[i] = now[i]; Prefs.bubbles = r; _bubbleFallback = null; }
            return Prefs.bubbles;
        }
        /// <summary>This character's bubble contents to CHANGE (never null, its arrays whole; the stored object, added when missing: call Save
        /// after the change). To read: <see cref="BubbleSetFound"/> (none: every bubble empty).</summary>
        public static BubbleSet BubbleSetOf(string character)
        {
            BubbleSet e = null;
            foreach (var s in Prefs.bubbleSets ?? new BubbleSet[0]) if (s != null && s.character == character) { e = s; break; }
            if (e == null) { e = new BubbleSet { character = character ?? "" }; var list = new List<BubbleSet>(Prefs.bubbleSets ?? new BubbleSet[0]) { e }; Prefs.bubbleSets = list.ToArray(); }
            if (e.ids == null || e.ids.Length != BubbleCount) e.ids = Fit(e.ids, BubbleCount);
            if (e.item == null || e.item.Length != BubbleCount) e.item = Fit(e.item, BubbleCount);
            return e;
        }
        /// <summary>The contents of this character as stored, without adding an entry (null: none yet; smoke, log).</summary>
        public static BubbleSet BubbleSetFound(string character)
        {
            foreach (var s in Prefs.bubbleSets ?? new BubbleSet[0]) if (s != null && s.character == character) return s;
            return null;
        }

        /// <summary>Item 6: finds the saved place for menu <paramref name="key"/>, or null if not yet saved.</summary>
        public static MenuPlace MenuPlaceOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var m in Prefs.menuPlaces ?? new MenuPlace[0])
                if (m != null && m.key == key && m.place != null) return m;
            return null;
        }
        /// <summary>Item 6: saves a menu's place and scale under <paramref name="key"/>.</summary>
        public static void SetMenuPlace(string key, PadPlace place, float scale = 1f)
        {
            if (string.IsNullOrEmpty(key) || place == null) return;
            var s = Prefs;
            var list = new List<MenuPlace>(s.menuPlaces ?? new MenuPlace[0]);
            var found = list.Find(m => m != null && m.key == key);
            if (found == null) { found = new MenuPlace { key = key, place = place, scale = scale }; list.Add(found); s.menuPlaces = list.ToArray(); }
            else { found.place = place; found.scale = scale; }
        }
        /// <summary>Item 6: clears all saved menu places (used when resetting panel places).</summary>
        public static void ClearMenuPlaces()
        {
            Prefs.menuPlaces = new MenuPlace[0];
        }

        /// <summary>Forgets what was read (tests; a scene reload keeps the static copy, which is what was last saved).</summary>
        public static void Reload() { _data = null; }
    }
}
