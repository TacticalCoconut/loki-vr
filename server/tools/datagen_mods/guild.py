"""
The char server's guild database (rAthena e985006, RENEWAL) for the Loki Online module:

  guild_exp   char/int_guild.cpp GuildExpDatabase (db/exp_guild.yml -> db/re/exp_guild.yml -> db/import/exp_guild.yml,
              TypesafeYamlDatabase<uint16, s_guild_exp_db> "GUILD_EXP_DB" version 1) with loadingFinished's check.

The char server loads it in inter_guild_sql_init (do_init -> inter_init_sql); guild_calcinfo reads it through
GuildExpDatabase::get_nextexp (0 for a missing level).
"""
import importlib.util
import os
import sys


def _mobs():
    mod = sys.modules.get('loki_datagen_mobs')
    if mod is None:
        spec = importlib.util.spec_from_file_location('loki_datagen_mobs', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'mobs.py'))
        mod = importlib.util.module_from_spec(spec)
        sys.modules['loki_datagen_mobs'] = mod
        spec.loader.exec_module(mod)
    return mod


M = _mobs()
YamlDb = M.YamlDb
warn = M.warn

# common/mmo.hpp MAX_GUILDLEVEL; config/const.hpp MAX_GUILD_EXP = INT32_MAX.
MAX_GUILDLEVEL = 50
MAX_GUILD_EXP = 2147483647


class GuildExpDb(YamlDb):
    """int_guild.cpp GuildExpDatabase (uint16 level -> exp)."""
    TYPE, VERSION = 'GUILD_EXP_DB', 1

    def default_location(self):
        return 'db/exp_guild.yml'

    def parse_body_node(self, node):
        if not self.nodes_exist(node, ['Level', 'Exp']):
            return 0
        level = self.as_u16(node, 'Level')
        if level == 0:
            self.invalid('The minimum guild level is 1.')
            return 0
        if level >= MAX_GUILDLEVEL:
            self.invalid('Guild level %d exceeds maximum level %d, skipping.' % (level, MAX_GUILDLEVEL))
            return 0
        exp = self.as_u64(node, 'Exp')
        if exp > MAX_GUILD_EXP:
            self.invalid('Guild exp %d exceeds max of %d.' % (exp, MAX_GUILD_EXP))
            return 0
        e = self.find(level)
        if e is None:
            self.put(level, {'level': level, 'exp': exp})
        else:
            e['exp'] = exp
        return 1

    def loading_finished(self):
        # GuildExpDatabase::loadingFinished
        for level in range(1, MAX_GUILDLEVEL):
            e = self.find(level)
            if e is None or e['exp'] == 0:
                warn('Missing experience for guild level %d.' % level)


def build(ctx):
    db = GuildExpDb(ctx)
    db.load()
    # the oracle's char-server log: Done reading '49' entries in 'db/re/exp_guild.yml' (db/exp_guild.yml and the
    # import have no Body)
    M.report_counts('exp_guild', db.counts, [49])
    ctx['write_json']('guild_exp', {'exp': sorted([[k, e['exp']] for k, e in db.data.items()])})
