"""
Status, companion and world databases (rAthena e985006, RENEWAL) for the Loki Online module, loaded exactly as the map
server loads them (the shared YAML machinery, battle_config and the monster database come from mobs.py):

  misc_status       status.cpp StatusDatabase (db/re/status.yml + import) incl. loadingFinished (script -> SCB_ALL,
                    conditional/unconditional state fix, display_hallucination, StatusRelevantBLTypes), in iteration
                    order; SCDisabled from status_readdb_status_disabled (db/status_disabled.txt + import).
  misc_size_fix     status.cpp SizeFixDatabase (db/size_fix.yml -> db/re/size_fix.yml -> import).
  misc_attr_fix     status.cpp AttributeDatabase (db/attr_fix.yml -> db/re/attr_fix.yml -> import): attr_fix_table.
  misc_pet          pet.cpp PetDatabase.
  misc_homunculus   homunculus.cpp HomunculusDatabase and HomExpDatabase.
  misc_mercenary    mercenary.cpp MercenaryDatabase (db/mercenary_db.yml -> db/re/mercenary_db.yml -> import).
  misc_elemental    elemental.cpp ElementalDatabase.
  misc_castle       guild.cpp CastleDatabase (+ loadingFinished).
  misc_quest        quest.cpp QuestDatabase.
  misc_achievement  achievement.cpp AchievementDatabase (+ loadingFinished) and AchievementLevelDatabase.
  misc_battleground battleground.cpp BattlegroundDatabase.
  misc_instance     instance.cpp InstanceDatabase.

Scripts (status Script, pet Script/SupportScript, achievement Condition/Rewards Script) are kept as source text; the
script engine compiles them. Map references keep rAthena's value (mapindex or map id) plus the map name.
"""
import datetime
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
K, YamlDb, UMap, Fail = M.K, M.YamlDb, M.UMap, M.Fail
i16, u16, i32, u32, u8, cap_value, c_str, atoi = M.i16, M.u16, M.i32, M.u32, M.u8, M.cap_value, M.c_str, M.atoi
warn = M.warn


def bits_to_words(bits, nbits):
    """std::bitset<N> as little-endian u64 words (bit i in word i/64)."""
    words = [0] * ((nbits + 63) // 64)
    for b in bits:
        words[b // 64] |= 1 << (b % 64)
    return words


# ---------------------------------------------------------------------------------------------------- status.yml
class StatusDb(YamlDb):
    """status.cpp StatusDatabase (TypesafeCachedYamlDatabase<uint16, s_status_change_db>)."""
    TYPE, VERSION, MIN_VERSION = 'STATUS_DB', 4, 3

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world
        # constructor: SCB_BATTLE = all but SCB_BASE and SCB_DYE, SCB_ALL = all but SCB_DYE
        self.scb_all = set(range(K['SCB_MAX'])) - {K['SCB_DYE']}
        self.scb_battle = self.scb_all - {K['SCB_BASE']}
        self.relevant_bl = None

    def default_location(self):
        return 'db/status.yml'

    def validate(self, t):
        return K['SC_NONE'] < t < K['SC_MAX']

    def _flag_set(self, node, key, prefix, lo, hi, target, what):
        """States / CalcFlags / Flags: bitset set/reset per <name>: <bool>."""
        sub = node[key] or {}
        for name in list(sub):
            if key == 'CalcFlags' and name == 'All':
                continue
            c = self.const(prefix + name)
            if c is None:
                self.invalid('%s %s is invalid.' % (what, name))
                raise Fail()
            if c < lo or c >= hi:
                self.invalid('%s %s is out of bounds.' % (what, name))
                raise Fail()
            if self.as_bool(sub, name):
                target.add(c)
            else:
                target.discard(c)

    def _or_set(self, node, key, prefix, lo, hi, value, width):
        sub = node[key] or {}
        for name in list(sub):
            c = self.const(prefix + name)
            if c is None:
                self.invalid('%s %s is invalid.' % (key, name))
                raise Fail()
            if c < lo or c >= hi:
                self.invalid('%s %s is out of bounds.' % (key, name))
                raise Fail()
            value = M.wrap(value | c, width, False) if self.as_bool(sub, name) else M.wrap(value & ~c, width, False)
        return value

    def _sc_list(self, node, key, lst):
        sub = node[key] or {}
        for name in list(sub):
            c = self.const('SC_' + name)
            if c is None:
                self.invalid('%s status %s is invalid.' % (key, name))
                raise Fail()
            if not self.validate(c):
                self.invalid('%s status %s is out of bounds.' % (key, name))
                raise Fail()
            if self.as_bool(sub, name):
                lst.append(c)
            elif c in lst:
                lst.remove(c)

    def parse_body_node(self, node):
        name = self.as_str(node, 'Status')
        c = self.const('SC_' + name)
        if c is None:
            self.invalid('Invalid Status %s.' % name)
            return 0
        if not self.validate(c):
            self.invalid('Status %s is out of bounds.' % name)
            return 0
        sid = i32(c)
        st = self.find(u16(sid))
        exists = st is not None
        if not exists:
            st = {'type': sid, 'name': name, 'icon': K['EFST_BLANK'], 'state': set(), 'calc_flag': set(),
                  'opt1': 0, 'opt2': 0, 'opt3': 0, 'look': 0, 'flag': set(), 'skill_id': 0, 'endonstart': [],
                  'fail': [], 'endreturn': [], 'endonend': [], 'min_duration': 1, 'min_rate': 0, 'script': None}
        if self.exists(node, 'Icon'):
            icon = self.as_str(node, 'Icon')
            v = self.const(icon)
            if v is None:
                self.invalid('Icon %s is invalid, defaulting to EFST_BLANK.' % icon)
                v = K['EFST_BLANK']
            if v < K['EFST_BLANK'] or v >= K['EFST_MAX']:
                v = K['EFST_BLANK']
            st['icon'] = v
        elif not exists:
            st['icon'] = K['EFST_BLANK']
        if self.exists(node, 'DurationLookup'):
            sk = self.as_str(node, 'DurationLookup')
            sid2 = self.w['skills'].name2id(sk)
            if sid2 == 0:
                self.invalid('DurationLookup skill %s is invalid, defaulting to none.' % sk)
            st['skill_id'] = sid2
        elif not exists:
            st['skill_id'] = 0
        if self.exists(node, 'States'):
            self._flag_set(node, 'States', 'SCS_', K['SCS_NONE'], K['SCS_MAX'], st['state'], 'State')
        elif not exists:
            st['state'] = set()
        if self.exists(node, 'CalcFlags'):
            fl = node['CalcFlags'] or {}
            if self.exists(fl, 'All'):
                st['calc_flag'] = set(self.scb_all) if self.as_bool(fl, 'All') else set()
            self._flag_set(node, 'CalcFlags', 'SCB_', K['SCB_NONE'], K['SCB_MAX'], st['calc_flag'], 'CalcFlag')
        elif not exists:
            st['calc_flag'] = set()
        if self.exists(node, 'Opt1'):
            opt = self.as_str(node, 'Opt1')
            v = self.const('OPT1_' + opt)
            if v is None or v < K['OPT1_NONE'] or v >= K['OPT1_MAX']:
                self.invalid('Opt1 %s is invalid.' % opt)
                return 0
            st['opt1'] = v
        elif not exists:
            st['opt1'] = K['OPT1_NONE']
        if self.exists(node, 'Opt2'):
            st['opt2'] = self._or_set(node, 'Opt2', 'OPT2_', K['OPT2_NONE'], K['OPT2_MAX'], st['opt2'], 16)
        elif not exists:
            st['opt2'] = K['OPT2_NONE']
        if self.exists(node, 'Opt3'):
            st['opt3'] = self._or_set(node, 'Opt3', 'OPT3_', K['OPT3_NORMAL'], K['OPT3_MAX'], st['opt3'], 32)
        elif not exists:
            st['opt3'] = K['OPT3_NORMAL']
        if self.exists(node, 'Options'):
            st['look'] = self._or_set(node, 'Options', 'OPTION_', K['OPTION_NOTHING'], K['OPTION_MAX'], st['look'], 32)
        elif not exists:
            st['look'] = K['OPTION_NOTHING']
        if self.exists(node, 'Flags'):
            self._flag_set(node, 'Flags', 'SCF_', K['SCF_NONE'], K['SCF_MAX'], st['flag'], 'Flag')
        elif not exists:
            st['flag'] = set()
        if self.exists(node, 'MinRate'):
            st['min_rate'] = self.as_u16(node, 'MinRate')
        elif not exists:
            st['min_rate'] = 0
        if self.exists(node, 'MinDuration'):
            st['min_duration'] = self.as_i64(node, 'MinDuration')
        elif not exists:
            st['min_duration'] = 1
        for key, field in (('Fail', 'fail'), ('EndOnStart', 'endonstart'), ('EndReturn', 'endreturn'), ('EndOnEnd', 'endonend')):
            if self.exists(node, key):
                self._sc_list(node, key, st[field])
        if self.exists(node, 'Script'):
            st['script'] = self.as_str(node, 'Script')
        elif not exists:
            st['script'] = None
        if not exists:
            self.put(u16(sid), st)
        return 1

    def loading_finished(self):
        """StatusDatabase::loadingFinished."""
        conf = self.w['conf']
        rel = [K['BL_PC']] * K['EFST_MAX']
        c = self.ctx['consts']
        pairs = [(c['scs_' + a], c['scs_' + a + 'cond']) for a in (
            'nomove', 'nopickitem', 'nodropitem', 'nocast', 'nochat', 'noequipitem', 'nounequipitem', 'noconsumeitem',
            'noattack', 'nowarp', 'nodeathpenalty', 'nointeract')]
        for key, st in self.data.items():
            if st['script'] is not None:
                st['calc_flag'] |= self.scb_all
            for uncond, cond in pairs:
                if cond in st['state'] and uncond in st['state']:
                    warn('StatusDatabase::loadingFinished: conditional and unconditional state for SC %d; removing the conditional one' % key)
                    st['state'].discard(cond)
            if st['type'] == c['sc_hallucination'] and not conf['display_hallucination']:
                st['icon'] = K['EFST_BLANK']
            if st['icon'] == K['EFST_BLANK']:
                continue
            if c['scf_bleffect'] in st['flag']:
                rel[st['icon']] |= K['BL_SCEFFECT']
            else:
                rel[st['icon']] = K['BL_PC']
        self.relevant_bl = rel


def read_status_disabled(ctx):
    """status.cpp status_readdb_status_disabled over db/status_disabled.txt then db/import/ (SCDisabled[type])."""
    disabled, counts = {}, []
    for rel in ('db/status_disabled.txt', 'db/import/status_disabled.txt'):
        if not os.path.exists(ctx['resolve'](rel)):
            continue
        n = 0
        for _, s in M.sv_readdb_rows(ctx, rel, ',', 2, 2):
            if s[0][:1].isdigit():
                t = atoi(s[0])
            else:
                t = ctx['consts'].get(M.lower(s[0]), K['SC_NONE'])
            if t <= K['SC_NONE'] or t >= K['SC_MAX']:
                warn('status_readdb_status_disabled: Invalid SC with type %s.' % s[0])
                continue
            disabled[t] = u16(u32(M.strtol(s[1], 10)[0]))  # (uint32)atol(...) stored into uint16 SCDisabled[]
            n += 1
        counts.append((rel, n))
    return disabled, counts


class SizeFixDb(YamlDb):
    """status.cpp SizeFixDatabase (int32 weapon type -> s_sizefix_db)."""
    TYPE, VERSION = 'SIZE_FIX_DB', 1

    def default_location(self):
        return 'db/size_fix.yml'

    def parse_body_node(self, node):
        name = self.as_str(node, 'Weapon')
        c = self.const('W_' + name)
        if c is None:
            self.invalid('Size Fix unknown weapon %s, skipping.' % name)
            return 0
        if c < K['W_FIST'] or c > K['W_2HSTAFF']:
            self.invalid('Size Fix weapon %s is an invalid weapon, skipping.' % name)
            return 0
        size = self.find(c)
        exists = size is not None
        if not exists:
            size = {'small': 0, 'medium': 0, 'large': 0}
        for key, field in (('Small', 'small'), ('Medium', 'medium'), ('Large', 'large')):
            if self.exists(node, key):
                v = self.as_u16(node, key)
                if v > 100:
                    self.invalid('%s Size Fix %d for %s is out of bounds, defaulting to 100.' % (key, v, name))
                    v = 100
                size[field] = v
            elif not exists:
                size[field] = 100
        if not exists:
            self.put(c, size)
        return 1


ELEMENT_NAMES = [('Neutral', 0), ('Water', 1), ('Earth', 2), ('Fire', 3), ('Wind', 4), ('Poison', 5), ('Holy', 6),
                 ('Dark', 7), ('Ghost', 8), ('Undead', 9)]  # map.hpp um_eleid2elename


class AttributeDb(YamlDb):
    """status.cpp AttributeDatabase: attr_fix_table[MAX_ELE_LEVEL][ELE_MAX][ELE_MAX], 100 until set."""
    TYPE, VERSION = 'ATTRIBUTE_DB', 1

    def __init__(self, ctx):
        super().__init__(ctx)
        self.clear()

    def clear(self):
        self.table = [[[100] * K['ELE_MAX'] for _ in range(K['ELE_MAX'])] for _ in range(K['MAX_ELE_LEVEL'])]

    def default_location(self):
        return 'db/attr_fix.yml'

    def parse_body_node(self, node):
        level = self.as_u16(node, 'Level')
        if not (1 <= level <= K['MAX_ELE_LEVEL']):
            self.invalid('Invalid element level %d.' % level)
            return 0
        for atk_name, atk in ELEMENT_NAMES:
            if not self.exists(node, atk_name):
                continue
            ele_node = node[atk_name]
            for def_name, dfe in ELEMENT_NAMES:
                if not self.exists(ele_node, def_name):
                    continue
                v = self.as_i16(ele_node, def_name)
                self.table[level - 1][atk][dfe] = cap_value(v, -100, 200)
        return 1


# -------------------------------------------------------------------------------------------------------- pet_db
class PetDb(YamlDb):
    """pet.cpp PetDatabase (uint16 mob id -> s_pet_db)."""
    TYPE, VERSION = 'PET_DB', 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/pet_db.yml'

    def _item(self, node, key, what):
        name = self.as_str(node, key)
        item = self.w['items'].search_aegisname(name)
        if item is None:
            self.invalid('%s %s does not exist.' % (what, name))
            raise Fail()
        return item['nameid']

    def parse_body_node(self, node):
        mob_name = self.as_str(node, 'Mob')
        mob = self.w['mob_db'].search_aegisname(mob_name)
        if mob is None:
            self.invalid('Mob %s does not exist and cannot be used as a pet.' % mob_name)
            return 0
        mob_id = u16(mob['id'])
        pet = self.find(mob_id)
        exists = pet is not None
        if not exists:
            if not self.nodes_exist(node, ['EggItem', 'Fullness', 'CaptureRate']):
                return 0
            pet = {'class_': mob_id, 'item_id': 0, 'egg_id': 0, 'acce_id': 0, 'food_id': 0, 'fullness': 0,
                   'hungry_delay': 0, 'hunger_increase': 0, 'r_hungry': 0, 'r_full': 0, 'intimate': 0, 'die': 0,
                   'hungry_intimacy_dec': 0, 'capture': 0, 's_perfor': False, 'attack_rate': 0,
                   'defence_attack_rate': 0, 'change_target_rate': 0, 'allow_autofeed': False,
                   'evolution_data': UMap(), 'pet_support_script': None, 'pet_bonus_script': None}
        if self.exists(node, 'TameItem'):
            pet['item_id'] = self._item(node, 'TameItem', 'Taming item')
        elif not exists:
            pet['item_id'] = 0
        if self.exists(node, 'EggItem'):
            pet['egg_id'] = self._item(node, 'EggItem', 'Egg item')
        if self.exists(node, 'EquipItem'):
            pet['acce_id'] = self._item(node, 'EquipItem', 'Equip item')
        elif not exists:
            pet['acce_id'] = 0
        if self.exists(node, 'FoodItem'):
            pet['food_id'] = self._item(node, 'FoodItem', 'Food item')
        elif not exists:
            pet['food_id'] = 0
        if self.exists(node, 'Fullness'):
            pet['fullness'] = min(self.as_u16(node, 'Fullness'), 100)
        if self.exists(node, 'HungryDelay'):
            pet['hungry_delay'] = u32(self.as_u32(node, 'HungryDelay') * 1000)
        elif not exists:
            pet['hungry_delay'] = 60000
        if self.exists(node, 'HungerIncrease'):
            pet['hunger_increase'] = self.as_i32(node, 'HungerIncrease')
        elif not exists:
            pet['hunger_increase'] = 20
        if self.exists(node, 'IntimacyStart'):
            pet['intimate'] = min(self.as_u32(node, 'IntimacyStart'), 10000)
        elif not exists:
            pet['intimate'] = 250
        for key, field, default in (('IntimacyFed', 'r_hungry', 50), ('IntimacyOverfed', 'r_full', -100),
                                    ('IntimacyHungry', 'hungry_intimacy_dec', -5), ('IntimacyOwnerDie', 'die', -20)):
            if self.exists(node, key):
                pet[field] = self.as_i32(node, key)
            elif not exists:
                pet[field] = default
        if self.exists(node, 'CaptureRate'):
            pet['capture'] = min(self.as_u16(node, 'CaptureRate'), 10000)
        if self.exists(node, 'SpecialPerformance'):
            pet['s_perfor'] = self.as_bool(node, 'SpecialPerformance')
        elif not exists:
            pet['s_perfor'] = True
        for key, field in (('AttackRate', 'attack_rate'), ('RetaliateRate', 'defence_attack_rate'), ('ChangeTargetRate', 'change_target_rate')):
            if self.exists(node, key):
                pet[field] = min(self.as_u16(node, key), 10000)
            elif not exists:
                pet[field] = 10001  # unreachable
        if self.exists(node, 'AllowAutoFeed'):
            pet['allow_autofeed'] = self.as_bool(node, 'AllowAutoFeed')
        elif not exists:
            pet['allow_autofeed'] = False
        if self.exists(node, 'Script'):
            pet['pet_bonus_script'] = self.as_str(node, 'Script')
        elif not exists:
            pet['pet_bonus_script'] = None
        if self.exists(node, 'SupportScript'):
            pet['pet_support_script'] = self.as_str(node, 'SupportScript')
        elif not exists:
            pet['pet_support_script'] = None
        if self.exists(node, 'Evolution'):
            for evo_node in node['Evolution'] or []:
                target = self.as_str(evo_node, 'Target')
                tmob = self.w['mob_db'].search_aegisname(target)
                if tmob is None:
                    self.invalid('Evolution target %s does not exist.' % target)
                    return 0
                tid = u16(tmob['id'])
                if not self.exists(evo_node, 'ItemRequirements'):
                    self.invalid('Missing required node "ItemRequirements".')
                    return 0
                evo = pet['evolution_data'].get(tid)
                evo_exists = evo is not None
                if not evo_exists:
                    evo = {'target_mob_id': tid, 'requirements': UMap()}
                for req in evo_node['ItemRequirements'] or []:
                    nameid = self._item(req, 'Item', 'Evolution requirement item')
                    amount = self.as_u16(req, 'Amount')
                    if amount > K['MAX_AMOUNT']:
                        self.invalid('Amount %d exceeds the maximum of %d.' % (amount, K['MAX_AMOUNT']))
                        return 0
                    evo['requirements'][nameid] = amount
                if not evo_exists:
                    pet['evolution_data'][tid] = evo
        if not exists:
            self.put(mob_id, pet)
        return 1


# ------------------------------------------------------------------------------------------------- homunculus
def hom_class2type(class_):
    """homunculus.cpp hom_class2type over hom_class2mapid (HT_REG 1, HT_EVO 2, HT_S 4; unknown classes map to -1,
    which has every bit set and so reads as HT_EVO)."""
    if class_ in (6048, 6049, 6050, 6051, 6052):
        return 4
    if class_ in range(6001, 6009):
        return 1
    return 2


STAT_NAMES = ['Hp', 'Sp', 'Str', 'Agi', 'Vit', 'Int', 'Dex', 'Luk']
STAT_FIELDS = {'Hp': 'hp', 'Sp': 'sp', 'Str': 'str', 'Agi': 'agi', 'Vit': 'vit', 'Int': 'int_', 'Dex': 'dex', 'Luk': 'luk'}


def hom_stats(hp=0):
    return {'hp': hp, 'sp': 0, 'str': 0, 'agi': 0, 'vit': 0, 'int_': 0, 'dex': 0, 'luk': 0}


class HomunculusDb(YamlDb):
    """homunculus.cpp HomunculusDatabase (int32 class -> s_homunculus_db)."""
    TYPE, VERSION = 'HOMUNCULUS_DB', 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/homunculus_db.yml'

    def _status(self, name, stat, node, bonus):
        """HomunculusDatabase::parseStatusNode."""
        v = self.as_u32(node, name)
        f = STAT_FIELDS[stat]
        bonus[f] = v if f in ('hp', 'sp') else u16(v)

    def parse_body_node(self, node):
        conf, skills = self.w['conf'], self.w['skills']
        class_name = self.as_str(node, 'Class')
        c = self.const('MER_' + class_name)
        if c is None:
            self.invalid('Invalid homunculus Class "%s", skipping.' % class_name)
            return 0
        class_id = i32(c)
        hom = self.find(class_id)
        exists = hom is not None
        if not exists:
            if not self.nodes_exist(node, ['Name', 'Status', 'SkillTree']):
                return 0
            hom = {'base_class': class_id, 'evo_class': 0, 'name': '', 'base': hom_stats(1), 'gmin': hom_stats(),
                   'gmax': hom_stats(), 'emin': hom_stats(), 'emax': hom_stats(), 'food_id': 0, 'base_aspd': 0,
                   'hungry_delay': 0, 'element': 0, 'race': 0, 'base_size': 0, 'evo_size': 0, 'skill_tree': []}
        if self.exists(node, 'Name'):
            hom['name'] = M.safestrncpy(self.as_str(node, 'Name'), K['NAME_LENGTH'])
        if self.exists(node, 'EvolutionClass'):
            evo = self.as_str(node, 'EvolutionClass')
            v = self.const('MER_' + evo)
            if v is None:
                self.invalid('Invalid homunculus Evolution Class %s, skipping.' % evo)
                return 0
            hom['evo_class'] = i32(v)
        elif not exists:
            hom['evo_class'] = class_id
        if self.exists(node, 'Food'):
            food = self.as_str(node, 'Food')
            item = self.w['items'].search_aegisname(food)
            if item is None:
                self.invalid('Invalid homunculus Food %s, skipping.' % food)
                return 0
            hom['food_id'] = item['nameid']
        elif not exists:
            hom['food_id'] = K['ITEMID_PET_FOOD']
        if self.exists(node, 'HungryDelay'):
            hom['hungry_delay'] = self.as_i32(node, 'HungryDelay')
        elif not exists:
            hom['hungry_delay'] = 60000
        for key, prefix, field, default in (('Race', 'RC_', 'race', K['RC_DEMIHUMAN']), ('Element', 'ELE_', 'element', K['ELE_NEUTRAL'])):
            if self.exists(node, key):
                name = self.as_str(node, key)
                v = self.const(prefix + name)
                if v is None:
                    self.invalid('Invalid homunculus %s %s, skipping.' % (key, name))
                    return 0
                hom[field] = v
            elif not exists:
                hom[field] = default
        # Size and EvolutionSize both write base_size (evo_size is never set); a new entry without EvolutionSize
        # ends up with SZ_MEDIUM whatever Size said.
        for key, default in (('Size', K['SZ_SMALL']), ('EvolutionSize', K['SZ_MEDIUM'])):
            if self.exists(node, key):
                name = self.as_str(node, key)
                v = self.const('SIZE_' + name)
                if v is None:
                    self.invalid('Invalid homunculus %s %s, skipping.' % (key, name))
                    return 0
                hom['base_size'] = v
            elif not exists:
                hom['base_size'] = default
        if self.exists(node, 'AttackDelay'):
            hom['base_aspd'] = min(self.as_u16(node, 'AttackDelay'), K['MIN_ASPD'])
        elif not exists:
            hom['base_aspd'] = 700
        if self.exists(node, 'Status'):
            for sn in node['Status'] or []:
                if not self.exists(sn, 'Type'):
                    return 0
                stat = self.as_str(sn, 'Type')
                if stat not in STAT_NAMES:
                    self.invalid('Invalid Status Type %s, skipping.' % stat)
                    return 0
                for key, field, dflt in (('Base', 'base', 1), ('GrowthMinimum', 'gmin', 0), ('GrowthMaximum', 'gmax', 0),
                                         ('EvolutionMinimum', 'emin', 0), ('EvolutionMaximum', 'emax', 0)):
                    if self.exists(sn, key):
                        self._status(key, stat, sn, hom[field])
                    elif not exists:
                        hom[field] = hom_stats(dflt)
            for lo, hi in (('gmin', 'gmax'), ('emin', 'emax')):
                for f in ('hp', 'sp', 'str', 'agi', 'vit', 'int_', 'dex', 'luk'):
                    if hom[lo][f] > hom[hi][f]:
                        hom[lo][f] = hom[hi][f]
        if self.exists(node, 'SkillTree'):
            for sk in node['SkillTree'] or []:
                entry = {'id': 0, 'max': 0, 'need_level': 0, 'intimacy': 0, 'evolution': False, 'need': UMap()}
                if self.exists(sk, 'Skill'):
                    sname = self.as_str(sk, 'Skill')
                    sid = skills.name2id(sname)
                    if sid == 0:
                        self.invalid('Invalid homunculus skill %s, skipping.' % sname)
                        return 0
                    if not (K['HM_SKILLBASE'] <= sid < K['HM_SKILLBASE'] + K['MAX_HOMUNSKILL']):
                        self.invalid('Homunculus skill %s (%d) is out of the homunculus skill range, skipping.' % (sname, sid))
                        return 0
                    entry['id'] = sid
                if self.exists(sk, 'Clear'):
                    before = len(hom['skill_tree'])
                    hom['skill_tree'] = [e for e in hom['skill_tree'] if e['id'] != entry['id']]
                    if len(hom['skill_tree']) == before:
                        self.invalid('Failed to remove nonexistent skill %d from homunculus %s.' % (entry['id'], class_name))
                    continue
                if self.exists(sk, 'MaxLevel'):
                    lv = self.as_u16(sk, 'MaxLevel')
                    entry['max'] = min(lv, skills.get_max(entry['id']))
                if self.exists(sk, 'RequiredLevel'):
                    lv = self.as_u16(sk, 'RequiredLevel')
                    cmax = conf['hom_S_max_level'] if hom_class2type(class_id) == 4 else conf['hom_max_level']
                    entry['need_level'] = min(lv, u16(cmax))
                if self.exists(sk, 'RequiredIntimacy'):
                    entry['intimacy'] = min(self.as_u16(sk, 'RequiredIntimacy'), 1000) * 100
                if self.exists(sk, 'RequireEvolution'):
                    evo = self.as_bool(sk, 'RequireEvolution')
                    if evo and hom['base_class'] == hom['evo_class']:
                        self.invalid('Homunculus %s does not have any evolution making skill %d unobtainable, skipping.' % (class_name, entry['id']))
                        return 0
                    entry['evolution'] = evo
                if self.exists(sk, 'Required'):
                    for pre in sk['Required'] or []:
                        psid, plv = 0, 0
                        if self.exists(pre, 'Skill'):
                            pname = self.as_str(pre, 'Skill')
                            psid = skills.name2id(pname)
                            if psid == 0:
                                self.invalid('Invalid homunculus skill %s, skipping.' % pname)
                                return 0
                            if not (K['HM_SKILLBASE'] <= psid < K['HM_SKILLBASE'] + K['MAX_HOMUNSKILL']):
                                self.invalid('Homunculus skill %s out of range, skipping.' % pname)
                                return 0
                        if self.exists(pre, 'Clear'):
                            found = False
                            for e in hom['skill_tree']:
                                if psid in e['need']:
                                    e['need'].pop(psid)
                                    found = True
                            if not found:
                                self.invalid('Failed to remove nonexistent prerequisite skill %d.' % psid)
                            continue
                        if self.exists(pre, 'Level'):
                            plv = self.as_u16(pre, 'Level')
                        if psid > 0 and plv > 0 and psid not in entry['need']:  # emplace keeps an existing key
                            entry['need'][psid] = plv
                hom['skill_tree'].append(entry)
        if not exists:
            self.put(class_id, hom)
        return 1


class HomExpDb(YamlDb):
    """homunculus.cpp HomExpDatabase (uint16 level -> exp)."""
    TYPE, VERSION = 'HOMUN_EXP_DB', 1

    def default_location(self):
        return 'db/exp_homun.yml'

    def parse_body_node(self, node):
        if not self.nodes_exist(node, ['Level', 'Exp']):
            return 0
        level = self.as_u16(node, 'Level')
        exp = self.as_u64(node, 'Exp')
        if level == 0:
            self.invalid('The minimum level is 1.')
            return 0
        if level >= K['MAX_LEVEL']:
            self.invalid('Homunculus level %d exceeds maximum level %d, skipping.' % (level, K['MAX_LEVEL']))
            return 0
        e = self.find(level)
        if e is None:
            self.put(level, {'level': level, 'exp': exp})
        else:
            e['exp'] = exp
        return 1


# ----------------------------------------------------------------------------------------- mercenary / elemental
class MercenaryDb(YamlDb):
    """mercenary.cpp MercenaryDatabase (int32 class -> s_mercenary_db; skill is an unordered_map id -> level)."""
    TYPE, VERSION = 'MERCENARY_DB', 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/mercenary_db.yml'

    def parse_body_node(self, node):
        mid = self.as_u32(node, 'Id')
        merc = self.find(i32(mid))
        exists = merc is not None
        if not exists:
            if not self.nodes_exist(node, ['AegisName', 'Name']):
                return 0
            merc = {'class_': i32(mid), 'sprite': '', 'name': '', 'lv': 0, 'range2': 0, 'range3': 0,
                    'status': M.new_status(), 'vd': {'look': [0] * K['LOOK_MAX'], 'sex': 0, 'dead_sit': 0},
                    'skill': UMap()}
        st = merc['status']
        if self.exists(node, 'AegisName'):
            merc['sprite'] = c_str(self.as_str(node, 'AegisName'), K['NAME_LENGTH'])
        if self.exists(node, 'Name'):
            merc['name'] = c_str(self.as_str(node, 'Name'), K['NAME_LENGTH'])
        if self.exists(node, 'Level'):
            merc['lv'] = min(self.as_u16(node, 'Level'), K['MAX_LEVEL'])
        elif not exists:
            merc['lv'] = 1
        for key, field, default in (('Hp', 'max_hp', 1), ('Sp', 'max_sp', 1)):
            if self.exists(node, key):
                st[field] = self.as_u32(node, key)
            elif not exists:
                st[field] = default
        if self.exists(node, 'Attack'):
            st['rhw']['atk'] = self.as_u16(node, 'Attack')
        elif not exists:
            st['rhw']['atk'] = 0
        if self.exists(node, 'Attack2'):
            st['rhw']['atk2'] = u16(st['rhw']['atk'] + self.as_u16(node, 'Attack2'))
        elif not exists:
            st['rhw']['atk2'] = st['rhw']['atk']
        for key, field in (('Defense', 'def'), ('MagicDefense', 'mdef')):
            if self.exists(node, key):
                st[field] = i16(cap_value(self.as_i32(node, key), K['DEFTYPE_MIN'], K['DEFTYPE_MAX']))
            elif not exists:
                st[field] = 0
        for key, field in (('Str', 'str'), ('Agi', 'agi'), ('Vit', 'vit'), ('Int', 'int_'), ('Dex', 'dex'), ('Luk', 'luk')):
            if self.exists(node, key):
                st[field] = i16(self.as_u16(node, key))
            elif not exists:
                st[field] = 1
        if self.exists(node, 'AttackRange'):
            st['rhw']['range'] = self.as_u16(node, 'AttackRange')
        elif not exists:
            st['rhw']['range'] = 0
        for key, field in (('SkillRange', 'range2'), ('ChaseRange', 'range3')):
            if self.exists(node, key):
                merc[field] = self.as_u16(node, key)
            elif not exists:
                merc[field] = 0
        if self.exists(node, 'Size'):
            v = self.const('Size_' + self.as_str(node, 'Size'))
            if v is None or v < K['SZ_SMALL'] or v > K['SZ_BIG']:
                v = K['SZ_SMALL']
            st['size'] = v
        elif not exists:
            st['size'] = K['SZ_SMALL']
        if self.exists(node, 'Race'):
            v = self.const('RC_' + self.as_str(node, 'Race'))
            if v is None or not (-1 < v < K['RC_MAX']):
                v = K['RC_FORMLESS']
            st['race'] = v
        elif not exists:
            st['race'] = K['RC_FORMLESS']
        if self.exists(node, 'Element'):
            v = self.const('ELE_' + self.as_str(node, 'Element'))
            if v is None or not (-1 < v < K['ELE_MAX']):
                v = K['ELE_NEUTRAL']
            st['def_ele'] = v
        elif not exists:
            st['def_ele'] = K['ELE_NEUTRAL']
        if self.exists(node, 'ElementLevel'):
            lv = self.as_u16(node, 'ElementLevel')
            st['ele_lv'] = u8(lv if 1 <= lv <= K['MAX_ELE_LEVEL'] else 1)
        elif not exists:
            st['ele_lv'] = 1
        if self.exists(node, 'WalkSpeed'):
            st['speed'] = cap_value(self.as_u16(node, 'WalkSpeed'), K['MIN_WALK_SPEED'], K['MAX_WALK_SPEED'])
        elif not exists:
            st['speed'] = K['DEFAULT_WALK_SPEED']
        if self.exists(node, 'AttackDelay'):
            st['adelay'] = cap_value(self.as_u16(node, 'AttackDelay'), K['MAX_ASPD_NOPC'], K['MIN_ASPD'])
        elif not exists:
            st['adelay'] = 4000
        if self.exists(node, 'AttackMotion'):
            st['amotion'] = cap_value(self.as_u16(node, 'AttackMotion'), 1, K['MIN_ASPD'] // K['AMOTION_DIVIDER_NOPC'])
        elif not exists:
            st['amotion'] = 2000
        if self.exists(node, 'DamageMotion'):
            st['dmotion'] = self.as_u16(node, 'DamageMotion')
        elif not exists:
            st['dmotion'] = 0
        st['aspd_rate'] = 1000
        if self.exists(node, 'Skills'):
            for sk in node['Skills'] or []:
                sname = self.as_str(sk, 'Name')
                sid = self.w['skills'].name2id(sname)
                if sid == 0:
                    self.invalid('Invalid skill %s, skipping.' % sname)
                    return 0
                if not (K['MC_SKILLBASE'] <= sid < K['MC_SKILLBASE'] + K['MAX_MERCSKILL']):
                    self.invalid('Skill %s (%d) is out of the mercenary skill range, skipping.' % (sname, sid))
                    return 0
                lv = self.as_u16(sk, 'MaxLevel')
                if lv == 0:
                    if merc['skill'].pop(sid, None) is None:
                        self.invalid('Failed to remove %s, the skill doesn\'t exist for mercenary %d.' % (sname, mid))
                    continue
                merc['skill'][sid] = lv
        if not exists:
            self.put(i32(mid), merc)
        return 1


ELEMENTAL_RANGES = ((2114, 2125), (20816, 20820))  # ELEMENTALID_AGNI_S..TERA_L, DILUVIO..SERPENS (elemental.hpp)


class ElementalDb(YamlDb):
    """elemental.cpp ElementalDatabase (int32 class -> s_elemental_db; skill is an unordered_map mode -> skill)."""
    TYPE, VERSION, MIN_VERSION = 'ELEMENTAL_DB', 2, 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/elemental_db.yml'

    def parse_body_node(self, node):
        eid = self.as_i32(node, 'Id')
        if not any(lo <= eid <= hi for lo, hi in ELEMENTAL_RANGES):
            self.invalid('Invalid Id %d.' % eid)
            return 0
        el = self.find(eid)
        exists = el is not None
        if not exists:
            if not self.nodes_exist(node, ['AegisName', 'Name', 'Size', 'Element', 'ElementLevel']):
                return 0
            el = {'class_': eid, 'sprite': '', 'name': '', 'lv': 0, 'range2': 0, 'range3': 0,
                  'status': M.new_status(), 'vd': {'look': [0] * K['LOOK_MAX'], 'sex': 0, 'dead_sit': 0},
                  'damagetaken': 0, 'skill': UMap()}
            el['vd']['look'][0] = eid
        st = el['status']
        if self.exists(node, 'AegisName'):
            el['sprite'] = c_str(self.as_str(node, 'AegisName'), K['NAME_LENGTH'])
        if self.exists(node, 'Name'):
            el['name'] = c_str(self.as_str(node, 'Name'), K['NAME_LENGTH'])
        if self.exists(node, 'Level'):
            el['lv'] = min(self.as_u16(node, 'Level'), K['MAX_LEVEL'])
        elif not exists:
            el['lv'] = 1
        for key, field, default in (('Hp', 'max_hp', 0), ('Sp', 'max_sp', 1)):
            if self.exists(node, key):
                st[field] = self.as_u32(node, key)
            elif not exists:
                st[field] = default
        if self.exists(node, 'Attack'):
            st['rhw']['atk'] = self.as_u16(node, 'Attack')
        elif not exists:
            st['rhw']['atk'] = 1
        if self.exists(node, 'Attack2'):
            st['rhw']['matk'] = self.as_u16(node, 'Attack2')  # RENEWAL
        elif not exists:
            st['rhw']['matk'] = 0
        for key, field in (('Defense', 'def'), ('MagicDefense', 'mdef')):
            if self.exists(node, key):
                st[field] = i16(cap_value(self.as_i32(node, key), K['DEFTYPE_MIN'], K['DEFTYPE_MAX']))
            elif not exists:
                st[field] = 0
        for key, field in (('Str', 'str'), ('Agi', 'agi'), ('Vit', 'vit'), ('Int', 'int_'), ('Dex', 'dex'), ('Luk', 'luk')):
            if self.exists(node, key):
                st[field] = i16(self.as_u16(node, key))
            elif not exists:
                st[field] = 0
        if self.exists(node, 'AttackRange'):
            st['rhw']['range'] = self.as_u16(node, 'AttackRange')
        elif not exists:
            st['rhw']['range'] = 0
        for key, field, default in (('SkillRange', 'range2', 5), ('ChaseRange', 'range3', 12)):
            if self.exists(node, key):
                el[field] = self.as_u16(node, key)
            elif not exists:
                el[field] = default
        if self.exists(node, 'Size'):
            v = self.const('Size_' + self.as_str(node, 'Size'))
            if v is None or v < K['SZ_SMALL'] or v > K['SZ_BIG']:
                v = K['SZ_SMALL']
            st['size'] = v
        if self.exists(node, 'Race'):
            v = self.const('RC_' + self.as_str(node, 'Race'))
            if v is None or not (-1 < v < K['RC_MAX']):
                v = K['RC_FORMLESS']
            st['race'] = v
        elif not exists:
            st['race'] = K['RC_FORMLESS']
        if self.exists(node, 'Element'):
            v = self.const('ELE_' + self.as_str(node, 'Element'))
            if v is None or not (-1 < v < K['ELE_MAX']):
                v = K['ELE_NEUTRAL']
            st['def_ele'] = v
        if self.exists(node, 'ElementLevel'):
            lv = self.as_u16(node, 'ElementLevel')
            st['ele_lv'] = u8(lv if 1 <= lv <= K['MAX_ELE_LEVEL'] else 1)
        if self.exists(node, 'WalkSpeed'):
            st['speed'] = cap_value(self.as_u16(node, 'WalkSpeed'), K['MIN_WALK_SPEED'], K['MAX_WALK_SPEED'])
        elif not exists:
            st['speed'] = 200
        if self.exists(node, 'AttackDelay'):
            st['adelay'] = cap_value(self.as_u16(node, 'AttackDelay'), K['MAX_ASPD_NOPC'], K['MIN_ASPD'])
        elif not exists:
            st['adelay'] = 504
        if self.exists(node, 'AttackMotion'):
            st['amotion'] = cap_value(self.as_u16(node, 'AttackMotion'), 1, K['MIN_ASPD'] // K['AMOTION_DIVIDER_NOPC'])
        elif not exists:
            st['amotion'] = 1020
        if self.exists(node, 'DamageMotion'):
            st['dmotion'] = self.as_u16(node, 'DamageMotion')
        elif not exists:
            st['dmotion'] = 360
        if self.exists(node, 'DamageTaken'):
            el['damagetaken'] = self.as_u16_rate(node, 'DamageTaken', 100)
        elif not exists:
            el['damagetaken'] = 100
        st['aspd_rate'] = 1000
        if self.exists(node, 'Mode'):
            modes = node['Mode'] or {}
            for mode_name in list(modes):
                v = self.const('EL_SKILLMODE_' + mode_name)
                if v is None or v < 1 or v > 4:  # EL_SKILLMODE_PASSIVE..EL_SKILLMODE_AGGRESSIVE
                    self.invalid('Invalid mode %s, skipping.' % mode_name)
                    continue
                entry = el['skill'].get(v)
                mode_exists = entry is not None
                if not mode_exists:
                    entry = {'id': 0, 'lv': 0}
                sn = modes[mode_name]
                sname = self.as_str(sn, 'Skill')
                sid = self.w['skills'].name2id(sname)
                if sid == 0:
                    self.invalid('Skipping invalid skill %s for mode %s.' % (sname, mode_name))
                    return 0
                if not (K['EL_SKILLBASE'] <= sid < K['EL_SKILLBASE'] + K['MAX_ELEMENTALSKILL']):
                    self.invalid('Skill %s (%d) is out of the skill range, skipping.' % (sname, sid))
                    return 0
                entry['id'] = sid
                if self.exists(sn, 'Level'):
                    lv = self.as_u16(sn, 'Level')
                    if lv > self.w['skills'].get_max(sid):
                        self.invalid('Skill %s\'s level %d exceeds max level.' % (sname, lv))
                        return 0
                    entry['lv'] = lv
                elif not mode_exists:
                    entry['lv'] = 1
                if entry['lv'] == 0:
                    el['skill'].pop(v, None)
                    return 0
                if not mode_exists:
                    el['skill'][v] = entry
        if not exists:
            self.put(eid, el)
        return 1


# ------------------------------------------------------------------------------------------------------ castle_db
class CastleDb(YamlDb):
    """guild.cpp CastleDatabase (int32 castle id -> guild_castle, the database fields)."""
    TYPE, VERSION, MIN_VERSION = 'CASTLE_DB', 2, 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/castle_db.yml'

    def parse_body_node(self, node):
        maps = self.w['maps']
        cid = self.as_i32(node, 'Id')
        gc = self.find(cid)
        exists = gc is not None
        if not exists:
            if not self.nodes_exist(node, ['Map', 'Name', 'Npc']):
                return 0
            gc = {'castle_id': cid, 'mapindex': 0, 'map_name': '', 'castle_name': '', 'castle_event': '', 'type': 0,
                  'client_id': 0, 'warp_enabled': False, 'warp_x': 0, 'warp_y': 0, 'zeny': 0, 'zeny_siege': 0}
        if self.exists(node, 'Map'):
            name = self.as_str(node, 'Map')
            idx = maps.name2idx(name)
            if idx == 0:
                self.invalid('Map %s doesn\'t exist, skipping.' % name)
                return 0
            gc['mapindex'], gc['map_name'] = idx, maps.idx2name(idx)
        if self.exists(node, 'Name'):
            gc['castle_name'] = M.safestrncpy(self.as_str(node, 'Name'), K['NAME_LENGTH'])
        if self.exists(node, 'Npc'):
            npc = self.as_str(node, 'Npc')
            if len(npc) > K['NPC_NAME_LENGTH']:
                self.invalid('Npc name %s too long, skipping.' % npc)
                return 0
            gc['castle_event'] = M.safestrncpy(npc, K['NPC_NAME_LENGTH'])
        if self.exists(node, 'Type'):
            t = self.as_str(node, 'Type')
            v = self.const('WOE_' + t)
            if v is None or v < K['WOE_FIRST_EDITION'] or v >= K['WOE_MAX']:
                self.invalid('Invalid WoE type %s.' % t)
                return 0
            gc['type'] = v
        elif not exists:
            gc['type'] = K['WOE_FIRST_EDITION']
        if self.exists(node, 'ClientId'):
            gc['client_id'] = self.as_u16(node, 'ClientId')
        elif not exists:
            gc['client_id'] = 0
        if self.exists(node, 'WarpEnabled'):
            gc['warp_enabled'] = self.as_bool(node, 'WarpEnabled')
        elif not exists:
            gc['warp_enabled'] = False
        for key, field, dim in (('WarpX', 'warp_x', 0), ('WarpY', 'warp_y', 1)):
            if self.exists(node, key):
                v = self.as_u16(node, key)
                if v == 0:
                    self.invalid('%s has to be greater than zero.' % key)
                    return 0
                size = maps.size(maps.mapindex2mapid(gc['mapindex']))
                if size is not None and v >= size[dim]:
                    self.invalid('%s has to be smaller than %d.' % (key, size[dim]))
                    return 0
                gc[field] = v
            elif not exists:
                gc[field] = 0
        for key, field, default in (('WarpCost', 'zeny', 100), ('WarpCostSiege', 'zeny_siege', 100000)):
            if self.exists(node, key):
                v = self.as_u32(node, key)
                if v > K['MAX_ZENY']:
                    self.invalid('%s has to be smaller than %d.' % (key, K['MAX_ZENY']))
                    return 0
                gc[field] = v
            elif not exists:
                gc[field] = default
        if not exists:
            self.put(cid, gc)
        return 1

    def loading_finished(self):
        for _, c in self.data.items():
            if c['client_id'] != 0:
                for _, c2 in self.data.items():
                    if c['castle_id'] != c2['castle_id'] and c['client_id'] == c2['client_id']:
                        warn('Castle ClientId %d is ambigous.' % c['client_id'])
                        break
            if c['warp_enabled'] and (c['client_id'] == 0 or c['warp_x'] == 0 or c['warp_y'] == 0):
                warn('Warping to castle %d is enabled, but its ClientId/WarpX/WarpY is not set. Disabling...' % c['castle_id'])
                c['warp_enabled'] = False


# ------------------------------------------------------------------------------------------------------- quest_db
def solve_time(s, now=None):
    """common/timer.cpp solve_time: seconds from now to now + the offsets (localtime/mktime; the server clock is UTC)."""
    now = now or datetime.datetime(2000, 1, 1)
    tm = [now.year, now.month - 1, now.day, now.hour, now.minute, now.second]
    i = 0
    while i < len(s):
        value = atoi(s[i:])
        if value == 0:
            i += 1
            continue
        if s[i] in '+-':
            i += 1
        while i < len(s) and '0' <= s[i] <= '9':
            i += 1
        c = s[i] if i < len(s) else ''
        n = s[i + 1] if i + 1 < len(s) else ''
        if c == 's':
            tm[5] += value; i += 1
        elif c == 'n':
            tm[4] += value; i += 1
        elif c == 'm' and n == 'n':
            tm[4] += value; i += 2
        elif c == 'h':
            tm[3] += value; i += 1
        elif c in 'dj' and c:
            tm[2] += value; i += 1
        elif c == 'm':
            tm[1] += value; i += 1
        elif c in 'ya' and c:
            tm[0] += value; i += 1
        elif c:
            i += 1
    y = tm[0] + tm[1] // 12
    mo = tm[1] % 12
    then = datetime.datetime(y, mo + 1, 1) + datetime.timedelta(days=tm[2] - 1, hours=tm[3], minutes=tm[4], seconds=tm[5])
    return (then - now).total_seconds()


def split_exact_quest_time(s):
    """quest.cpp split_exact_quest_time -> (week, day, hour, minute, second) or None."""
    w = d = h = mn = sec = -1
    i, low = 0, M.lower(s)
    days = [('sunday', 0), ('monday', 1), ('tuesday', 2), ('wednesday', 3), ('thursday', 4), ('friday', 5), ('saturday', 6)]
    while i < len(s):
        value = atoi(s[i:])
        if s[i] in '-+':
            i += 1
        while i < len(s) and '0' <= s[i] <= '9':
            i += 1
        rest = low[i:]
        hit = next(((n, v) for n, v in days if rest.startswith(n)), None)
        if hit:
            w, i = hit[1], i + len(hit[0])
        elif s[i:i + 1] == 's':
            sec, i = value, i + 1
        elif s[i:i + 2] == 'mn':
            mn, i = value, i + 2
        elif s[i:i + 1] == 'h':
            h, i = value, i + 1
        elif s[i:i + 1] in ('d', 'j'):
            d, i = value, i + 1
        elif i < len(s):
            i += 1
    if h < 0 or h > 23 or mn > 59 or sec > 59:
        return None
    return w, max(0, d), h, max(0, mn), max(0, sec)


class QuestDb(YamlDb):
    """quest.cpp QuestDatabase (uint32 id -> s_quest_db)."""
    TYPE, VERSION, MIN_VERSION = 'QUEST_DB', 3, 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/quest_db.yml'

    def parse_body_node(self, node):
        mobs, maps, items = self.w['mob_db'], self.w['maps'], self.w['items']
        qid = self.as_u32(node, 'Id')
        q = self.find(qid)
        exists = q is not None
        if not exists:
            if not self.nodes_exist(node, ['Title']):
                return 0
            q = {'id': i32(qid), 'time': 0, 'time_at': False, 'time_week': 0, 'objectives': [], 'dropitem': [], 'name': ''}
        if self.exists(node, 'Title'):
            q['name'] = self.as_str(node, 'Title')
        if self.exists(node, 'TimeLimit'):
            t = self.as_str(node, 'TimeLimit')
            if '+' in t:
                diff = solve_time(t)
                if diff <= 0:
                    self.invalid('Incorrect TimeLimit format %s given, skipping.' % t)
                    return 0
                q['time'] = int(diff)
            else:
                r = split_exact_quest_time(t)
                if r is None:
                    self.invalid('Incorrect TimeLimit format %s given, skipping.' % t)
                    return 0
                week, day, hour, minute, second = r
                q['time'] = hour * 3600 + minute * 60 + second if week > 0 else day * 86400 + hour * 3600 + minute * 60 + second
                q['time_at'] = True
                q['time_week'] = week
        elif not exists:
            q['time'], q['time_at'], q['time_week'] = 0, False, -1
        if self.exists(node, 'Targets'):
            for tn in node['Targets'] or []:
                if len(q['objectives']) >= K['MAX_QUEST_OBJECTIVES']:
                    self.invalid('Targets list exceeds the maximum of %d, skipping.' % K['MAX_QUEST_OBJECTIVES'])
                    return 0
                if not self.exists(tn, 'Mob') and not self.exists(tn, 'Id'):
                    self.invalid('Missing Target \'Mob\' or \'Id\', skipping.')
                    return 0
                index = mob_id = 0
                if self.exists(tn, 'Mob'):
                    name = self.as_str(tn, 'Mob')
                    mob = mobs.search_aegisname(name)
                    if mob is None:
                        self.invalid('Mob %s does not exist, skipping.' % name)
                        return 0
                    mob_id = u16(mob['id'])
                    target = next((o for o in q['objectives'] if o['mob_id'] == mob_id), None)
                else:
                    index = self.as_u16(tn, 'Id')
                    if index == 0:
                        self.invalid('\'Id\' can\'t be 0, skipping.')
                        return 0
                    target = next((o for o in q['objectives'] if o['index'] == index), None)
                target_exists = target is not None
                if not target_exists:
                    if not self.exists(tn, 'Count'):
                        self.invalid('Targets has no Count value specified, skipping.')
                        return 0
                    target = {'index': index, 'mob_id': mob_id, 'count': 0, 'min_level': 0, 'max_level': 0,
                              'race': K['RC_ALL'], 'size': K['SZ_ALL'], 'element': K['ELE_ALL'], 'mapid': -1,
                              'map_name': '', 'mobs_allowed': []}
                if not self.exists(tn, 'Mob'):
                    if self.exists(tn, 'MinLevel'):
                        target['min_level'] = self.as_u16(tn, 'MinLevel')
                    if self.exists(tn, 'MaxLevel'):
                        lv = self.as_u16(tn, 'MaxLevel')
                        if target['min_level'] > lv:
                            lv = K['MAX_LEVEL']
                        target['max_level'] = lv
                    if self.exists(tn, 'Race'):
                        r = self.as_str(tn, 'Race')
                        v = self.const('RC_' + r)
                        if v is None or v < K['RC_FORMLESS'] or v > K['RC_ALL'] or v in (K['RC_PLAYER_HUMAN'], K['RC_PLAYER_DORAM']):
                            self.invalid('Invalid race %s, skipping.' % r)
                            return 0
                        target['race'] = v
                    if self.exists(tn, 'Size'):
                        r = self.as_str(tn, 'Size')
                        v = self.const('Size_' + r)
                        if v is None or v < K['SZ_SMALL'] or v > K['SZ_ALL']:
                            self.invalid('Invalid size type %s, skipping.' % r)
                            return 0
                        target['size'] = v
                    if self.exists(tn, 'Element'):
                        r = self.as_str(tn, 'Element')
                        v = self.const('Ele_' + r)
                        if v is None or v < K['ELE_NEUTRAL'] or v > K['ELE_ALL']:
                            self.invalid('Invalid element %s, skipping.' % r)
                            return 0
                        target['element'] = v
                    if self.exists(tn, 'Location'):
                        loc = self.as_str(tn, 'Location')
                        idx = maps.name2idx(loc)
                        if idx == 0 and M.lower(loc) != 'all':
                            self.invalid('Map "%s" not found.' % loc)
                            return 0
                        target['mapid'] = maps.mapindex2mapid(idx)
                    if self.exists(tn, 'MapName'):
                        target['map_name'] = self.as_str(tn, 'MapName')
                    if self.exists(tn, 'MapMobTargets'):
                        mm = tn['MapMobTargets'] or {}
                        for name in list(mm):
                            mob = mobs.search_aegisname(name)
                            if mob is None:
                                self.invalid('Mob %s does not exist, skipping.' % name)
                                continue
                            mid = u16(mob['id'])
                            if not self.as_bool(mm, name):
                                if mid in target['mobs_allowed']:
                                    target['mobs_allowed'].remove(mid)
                                continue
                            if mid not in target['mobs_allowed']:
                                target['mobs_allowed'].append(mid)
                    if target['min_level'] == 0 and target['max_level'] > 0:
                        target['min_level'] = 1
                if self.exists(tn, 'Count'):
                    target['count'] = self.as_u16(tn, 'Count')
                q['objectives'].append(target)  # pushed again when it already existed (same shared entry twice)
        if self.exists(node, 'Drops'):
            for dn in node['Drops'] or []:
                mob_id = 0
                if self.exists(dn, 'Mob'):
                    name = self.as_str(dn, 'Mob')
                    mob = mobs.search_aegisname(name)
                    if mob is None:
                        self.invalid('Mob %s does not exist, skipping.' % name)
                        continue
                    mob_id = mob['id']
                target = next((d for d in q['dropitem'] if d['mob_id'] == u16(mob_id)), None)
                target_exists = target is not None
                if not target_exists:
                    if not self.exists(dn, 'Item'):
                        self.invalid('Drops has no Item value specified, skipping.')
                        continue
                    if not self.exists(dn, 'Rate'):
                        self.invalid('Drops has no Rate value specified, skipping.')
                        continue
                    target = {'nameid': 0, 'count': 0, 'rate': 0, 'mob_id': u16(mob_id)}
                if self.exists(dn, 'Item'):
                    name = self.as_str(dn, 'Item')
                    item = items.search_aegisname(name)
                    if item is None:
                        self.invalid('Item %s does not exist, skipping.' % name)
                        continue
                    target['nameid'] = item['nameid']
                if self.exists(dn, 'Count'):
                    count = self.as_u16(dn, 'Count')
                    if not items.isstackable(items.search(target['nameid'])):
                        self.invalid('Item is not stackable, capping to 1.')
                        count = 1
                    target['count'] = count
                elif not target_exists:
                    target['count'] = 1
                if self.exists(dn, 'Rate'):
                    target['rate'] = self.as_u16(dn, 'Rate')
                q['dropitem'].append(target)
        if not exists:
            self.put(qid, q)
        return 1


# ------------------------------------------------------------------------------------------------ achievement_db
class AchievementDb(YamlDb):
    """achievement.cpp AchievementDatabase (uint32 id -> s_achievement_db) and its achievement_mobs list."""
    TYPE, VERSION = 'ACHIEVEMENT_DB', 2

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world
        self.achievement_mobs = []

    def clear(self):
        super().clear()
        self.achievement_mobs = []

    def default_location(self):
        return 'db/achievement_db.yml'

    def mobexists(self, mob_id):
        return bool(self.w['conf']['feature_achievement']) and mob_id in self.achievement_mobs

    def parse_body_node(self, node):
        aid = self.as_u32(node, 'Id')
        a = self.find(aid)
        exists = a is not None
        if not exists:
            if not self.nodes_exist(node, ['Name']):
                return 0
            a = {'achievement_id': aid, 'name': '', 'group': 0, 'targets': {}, 'dependent_ids': [], 'condition': None,
                 'mapindex': -1, 'map_name': '', 'rewards': {'nameid': 0, 'amount': 0, 'script': None, 'title_id': 0},
                 'score': 0, 'has_dependent': 0}
        if self.exists(node, 'Group'):
            g = self.as_str(node, 'Group')
            v = self.const('AG_' + g)
            if v is None:
                self.invalid('Invalid Group %s for achievement %d, skipping.' % (g, aid))
                return 0
            a['group'] = v
        elif not exists:
            a['group'] = 0
        if self.exists(node, 'Name'):
            a['name'] = self.as_str(node, 'Name')
        if self.exists(node, 'Targets'):
            ag_battle, ag_taming = self.const('AG_BATTLE'), self.const('AG_TAMING')
            for tn in node['Targets'] or []:
                try:
                    tid = self.as_u16(tn, 'Id')
                except Fail:
                    continue
                if tid >= K['MAX_ACHIEVEMENT_OBJECTIVES']:
                    self.invalid('Target Id is out of valid range, skipping.')
                    continue
                target = a['targets'].get(tid)
                texists = target is not None
                if not texists:
                    if not self.exists(tn, 'Count') and not self.exists(tn, 'Mob'):
                        self.invalid('Target has no data specified, skipping.')
                        return 0
                    target = {'mob': 0, 'count': 0}
                if self.exists(tn, 'Count'):
                    count = self.as_u32(tn, 'Count')
                    if count == 0:
                        if texists:
                            del a['targets'][tid]
                            continue
                        self.invalid('Target count has to be > 0, skipping.')
                        return 0
                    target['count'] = i32(count)
                elif not texists:
                    target['count'] = 1
                if self.exists(tn, 'Mob'):
                    if a['group'] != ag_battle and a['group'] != ag_taming:
                        self.invalid('Target Mob is only supported for targets in group AG_BATTLE or AG_TAMING, skipping.')
                        continue
                    name = self.as_str(tn, 'Mob')
                    mob = self.w['mob_db'].search_aegisname(name)
                    if mob is None:
                        self.invalid('Target Mob %s does not exist, skipping.' % name)
                        return 0
                    if not self.mobexists(mob['id']):
                        self.achievement_mobs.append(mob['id'])
                    target['mob'] = mob['id']
                elif not texists:
                    target['mob'] = 0
                a['targets'][tid] = target
        if self.exists(node, 'Condition'):
            cond = self.as_str(node, 'Condition')
            if 'achievement_condition' not in cond:
                cond = 'achievement_condition( ' + cond + ' );'
            a['condition'] = cond
        elif not exists:
            a['condition'] = None
        if self.exists(node, 'Map'):
            if a['group'] != self.const('AG_CHATTING'):
                self.invalid('Map can only be used with the group AG_CHATTING, skipping.')
                return 0
            name = self.as_str(node, 'Map')
            a['mapindex'] = self.w['maps'].mapname2mapid(name)  # a map id despite the name
            if a['mapindex'] == -1:
                self.invalid('Map %s does not exist, skipping.' % name)
                return 0
            a['map_name'] = self.w['maps'].mapid2name(a['mapindex'])
        elif not exists:
            a['mapindex'] = -1
        if self.exists(node, 'Dependents'):
            dep = node['Dependents'] or {}
            for key in list(dep):
                did = M.c4_atou(key, 32) or 0
                active = self.as_bool(dep, str(did))  # looked up again by the number's canonical text
                if active:
                    if did in a['dependent_ids']:
                        self.invalid('Dependent achievement %d is already part of the list, skipping.' % did)
                        continue
                    if len(a['dependent_ids']) >= K['MAX_ACHIEVEMENT_DEPENDENTS']:
                        self.invalid('Maximum amount of dependent achievements reached, skipping.')
                        break
                    a['dependent_ids'].append(did)
                elif did in a['dependent_ids']:
                    a['dependent_ids'].remove(did)
        if self.exists(node, 'Rewards'):
            rn = node['Rewards']
            rw = a['rewards']
            if self.exists(rn, 'Item'):
                name = self.as_str(rn, 'Item')
                item = self.w['items'].search_aegisname(name)
                if item is None:
                    self.invalid('Reward Item %s does not exist, skipping.' % name)
                    return 0
                rw['nameid'] = item['nameid']
            if self.exists(rn, 'Amount'):
                rw['amount'] = self.as_u16(rn, 'Amount')
            elif not exists:
                rw['amount'] = 1
            if self.exists(rn, 'Script'):
                rw['script'] = self.as_str(rn, 'Script')
            elif not exists:
                rw['script'] = None
            if self.exists(rn, 'TitleId'):
                t = self.as_u32(rn, 'TitleId')
                if t < K['TITLE_BASE'] or t > K['TITLE_MAX']:
                    self.invalid('Reward Title ID %d does not exist, skipping.' % t)
                    return 0
                rw['title_id'] = t
            elif not exists:
                rw['title_id'] = 0
        if self.exists(node, 'Score'):
            a['score'] = i32(self.as_u32(node, 'Score'))
        elif not exists:
            a['score'] = 0
        if not exists:
            self.put(aid, a)
        return 1

    def loading_finished(self):
        """Invalid dependents are removed; the element after a removed one is not checked (dep++ after erase)."""
        for _, a in self.data.items():
            deps, i = a['dependent_ids'], 0
            while i < len(deps):
                if deps[i] not in self.data:
                    warn('achievement_read_db: An invalid Dependent ID %d was given for Achievement %d. Removing from list.' % (deps[i], a['achievement_id']))
                    del deps[i]
                    if i == len(deps):
                        break
                i += 1


class AchievementLevelDb(YamlDb):
    """achievement.cpp AchievementLevelDatabase (uint16 zero-based level -> points)."""
    TYPE, VERSION = 'ACHIEVEMENT_LEVEL_DB', 1

    def default_location(self):
        return 'db/achievement_level_db.yml'

    def parse_body_node(self, node):
        if not self.nodes_exist(node, ['Level', 'Points']):
            return 0
        level = self.as_u16(node, 'Level')
        if level == 0:
            self.invalid('Invalid achievement level 0 (minimum value: 1), skipping.')
            return 0
        level -= 1
        e = self.find(level)
        exists = e is not None
        if not exists:
            e = {'level': level, 'points': 0}
        e['points'] = self.as_u16(node, 'Points')
        if not exists:
            self.put(level, e)
        return 1


# ----------------------------------------------------------------------------------------------- battleground_db
def new_team():
    return {'warp_x': 0, 'warp_y': 0, 'quit_event': '', 'death_event': '', 'active_event': '', 'bg_id_var': ''}


class BattlegroundDb(YamlDb):
    """battleground.cpp BattlegroundDatabase (uint32 id -> s_battleground_type)."""
    TYPE, VERSION = 'BATTLEGROUND_DB', 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/battleground_db.yml'

    def parse_body_node(self, node):
        maps = self.w['maps']
        bid = self.as_u32(node, 'Id')
        bg = self.find(bid)
        exists = bg is not None
        if not exists:
            if not self.nodes_exist(node, ['Name', 'Locations']):
                return 0
            bg = {'id': i32(bid), 'name': '', 'required_players': 0, 'max_players': 0, 'min_lvl': 0, 'max_lvl': 0,
                  'maps': [], 'deserter_time': 0, 'start_delay': 0, 'solo': False, 'party': False, 'guild': False,
                  'job_restrictions': []}
        if self.exists(node, 'Name'):
            bg['name'] = c_str(self.as_str(node, 'Name'), K['NAME_LENGTH'])
        half = K['MAX_BG_MEMBERS'] // 2
        for key, field, default in (('MinPlayers', 'required_players', 1), ('MaxPlayers', 'max_players', half)):
            if self.exists(node, key):
                v = self.as_i32(node, key)
                if v < 1:
                    v = 1
                if v * 2 > K['MAX_BG_MEMBERS']:
                    v = half
                bg[field] = v
            elif not exists:
                bg[field] = default
        for key, field, default in (('MinLevel', 'min_lvl', 1), ('MaxLevel', 'max_lvl', K['MAX_LEVEL'])):
            if self.exists(node, key):
                bg[field] = min(self.as_i32(node, key), K['MAX_LEVEL'])
            elif not exists:
                bg[field] = default
        for key, field, default in (('Deserter', 'deserter_time', 600), ('StartDelay', 'start_delay', 0)):
            if self.exists(node, key):
                bg[field] = self.as_u32(node, key)
            elif not exists:
                bg[field] = default
        if self.exists(node, 'Join'):
            jn = node['Join']
            for key, field in (('Solo', 'solo'), ('Party', 'party'), ('Guild', 'guild')):
                if self.exists(jn, key):
                    bg[field] = self.as_bool(jn, key)
                elif not exists:
                    bg[field] = True
        elif not exists:
            bg['solo'] = bg['party'] = bg['guild'] = True
        if self.exists(node, 'JobRestrictions'):
            jobs = node['JobRestrictions'] or {}
            for name in list(jobs):
                v = self.const('JOB_' + name)
                if v is None:
                    self.invalid('Job %s does not exist.' % name)
                    continue
                if self.as_bool(jobs, name):
                    bg['job_restrictions'].append(i32(v))
                elif i32(v) in bg['job_restrictions']:
                    bg['job_restrictions'].remove(i32(v))
        if self.exists(node, 'Locations'):
            count = 0
            for loc in node['Locations'] or []:
                entry = {'id': 0, 'mapindex': 0, 'map_name': '', 'team1': new_team(), 'team2': new_team(),
                         'bgcallscript': '', 'is_reserved': False}
                if self.exists(loc, 'Map'):
                    name = self.as_str(loc, 'Map')
                    entry['mapindex'] = maps.name2idx(name)
                    if entry['mapindex'] == 0:
                        self.invalid('Invalid battleground map name %s, skipping.' % name)
                        return 0
                    if maps.mapindex2mapid(entry['mapindex']) < 0:
                        return 0
                    entry['map_name'] = maps.idx2name(entry['mapindex'])
                if self.exists(loc, 'StartEvent'):
                    ev = c_str(self.as_str(loc, 'StartEvent'), K['EVENT_NAME_LENGTH'])
                    if '::On' not in ev:
                        self.invalid('Battleground StartEvent label %s should begin with \'::On\', skipping.' % ev)
                        return 0
                    entry['bgcallscript'] = ev
                for team_key, team_field in (('TeamA', 'team1'), ('TeamB', 'team2')):
                    if not self.exists(loc, team_key):
                        continue
                    tn, team = loc[team_key], entry[team_field]
                    size = maps.size(maps.mapindex2mapid(entry['mapindex']))
                    for key, field, dim in (('RespawnX', 'warp_x', 0), ('RespawnY', 'warp_y', 1)):
                        if self.exists(tn, key):
                            v = self.as_u16(tn, key)
                            if v == 0:
                                self.invalid('%s has to be greater than zero.' % key)
                                return 0
                            if v >= size[dim]:
                                self.invalid('%s has to be smaller than %d.' % (key, size[dim]))
                                return 0
                            team[field] = v
                    for key, field in (('DeathEvent', 'death_event'), ('QuitEvent', 'quit_event'), ('ActiveEvent', 'active_event')):
                        if self.exists(tn, key):
                            ev = c_str(self.as_str(tn, key), K['EVENT_NAME_LENGTH'])
                            if '::On' not in ev:
                                self.invalid('Battleground %s label %s should begin with \'::On\', skipping.' % (key, ev))
                                return 0
                            team[field] = ev
                    if self.exists(tn, 'Variable'):
                        team['bg_id_var'] = c_str(self.as_str(tn, 'Variable'), K['NAME_LENGTH'])
                    entry['id'] = count
                    count += 1
                    entry['is_reserved'] = False
                bg['maps'].append(entry)
        if not exists:
            self.put(bid, bg)
        return 1


# ---------------------------------------------------------------------------------------------------- instance_db
class InstanceDb(YamlDb):
    """instance.cpp InstanceDatabase (int32 id -> s_instance_db)."""
    TYPE, VERSION, MIN_VERSION = 'INSTANCE_DB', 2, 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/instance_db.yml'

    def parse_body_node(self, node):
        maps = self.w['maps']
        iid = self.as_i32(node, 'Id')
        if iid <= 0:
            self.invalid('Instance Id is invalid, skipping.')
            return 0
        inst = self.find(iid)
        exists = inst is not None
        if not exists:
            if not self.nodes_exist(node, ['Name', 'Enter']):
                return 0
            inst = {'id': iid, 'name': '', 'limit': 0, 'timeout': 0, 'nonpc': False, 'nomapflag': False,
                    'destroyable': False, 'infinite_limit': False, 'infinite_timeout': False,
                    'enter': {'map': 0, 'x': 0, 'y': 0}, 'enter_map_name': '', 'maplist': [], 'maplist_names': []}
        if self.exists(node, 'Name'):
            name = self.as_str(node, 'Name')
            if any(o['name'] == name for o in self.data.values()):
                self.invalid('Instance name %s already exists, skipping.' % name)
                return 0
            inst['name'] = name
        for key, field, inf, default in (('TimeLimit', 'limit', 'infinite_limit', 3600), ('IdleTimeOut', 'timeout', 'infinite_timeout', 300)):
            if self.exists(node, key):
                v = self.as_i64(node, key)
                inst[field], inst[inf] = v, v == 0
            elif not exists:
                inst[field], inst[inf] = default, False
        for key, field, default in (('NoNpc', 'nonpc', False), ('NoMapFlag', 'nomapflag', False), ('Destroyable', 'destroyable', True)):
            if self.exists(node, key):
                inst[field] = self.as_bool(node, key)
            elif not exists:
                inst[field] = default
        if self.exists(node, 'Enter'):
            en = node['Enter']
            if not self.nodes_exist(en, ['Map', 'X', 'Y']):
                return 0
            name = self.as_str(en, 'Map')
            idx = maps.name2idx(name)
            if idx == 0:
                self.invalid('Map %s is not a valid map, skipping.' % name)
                return 0
            mapid = maps.mapindex2mapid(idx)
            if mapid < 0:
                return 0
            inst['enter']['map'], inst['enter_map_name'] = mapid, maps.mapid2name(mapid)
            size = maps.size(inst['enter']['map'])
            for key, field, dim in (('X', 'x', 0), ('Y', 'y', 1)):
                v = self.as_u16(en, key)
                if v == 0:
                    self.invalid('%s has to be greater than zero.' % key)
                    return 0
                if v >= size[dim]:
                    self.invalid('%s has to be smaller than %d.' % (key, size[dim]))
                    return 0
                inst['enter'][field] = v
        if self.exists(node, 'AdditionalMaps'):
            am = node['AdditionalMaps'] or {}
            for name in list(am):
                m = maps.mapname2mapid(name)
                if m == inst['enter']['map']:
                    self.invalid('Additional Map %s is already listed as the EnterMap.' % name)
                    continue
                if m == -1:
                    self.invalid('Additional Map %s is not a valid map, skipping.' % name)
                    return 0
                if self.as_bool(am, name):
                    inst['maplist'].append(m)
                elif m in inst['maplist']:
                    inst['maplist'].remove(m)
            inst['maplist_names'] = [maps.mapid2name(m) for m in inst['maplist']]
        if not exists:
            self.put(iid, inst)
        return 1


# --------------------------------------------------------------------------------------------------------- build
def build(ctx):
    w = M.world(ctx)
    compact, report = M.compact, M.report_counts

    # do_init_instance runs before the item and monster databases; it depends on neither
    inst = InstanceDb(ctx, w)
    inst.load()
    report('instance_db', inst.counts, [78])

    disabled, dcounts = read_status_disabled(ctx)
    report('status_disabled', dcounts, [402, 0])
    size_fix = SizeFixDb(ctx)
    size_fix.load()
    report('size_fix', size_fix.counts, [23, 2])
    status = StatusDb(ctx, w)
    status.load()
    report('status', status.counts, [1018])
    attr = AttributeDb(ctx)
    attr.load()
    report('attr_fix', attr.counts, [4])
    castle = CastleDb(ctx, w)
    castle.load()
    report('castle_db', castle.counts, [44])
    pet = PetDb(ctx, w)
    pet.load()
    report('pet_db', pet.counts, [107])
    hom = HomunculusDb(ctx, w)
    hom.load()
    report('homunculus_db', hom.counts, [13])
    hexp = HomExpDb(ctx)
    hexp.load()
    report('exp_homun', hexp.counts, [274])
    merc = MercenaryDb(ctx, w)
    merc.load()
    report('mercenary_db', merc.counts, [44, 20])
    elem = ElementalDb(ctx, w)
    elem.load()
    report('elemental_db', elem.counts, [17])
    quest = QuestDb(ctx, w)
    quest.load()
    report('quest_db', quest.counts, [4821])
    ach = AchievementDb(ctx, w)
    ach.load()
    report('achievement_db', ach.counts, [361])
    achl = AchievementLevelDb(ctx)
    achl.load()
    report('achievement_level_db', achl.counts, [20])
    bg = BattlegroundDb(ctx, w)
    bg.load()
    report('battleground_db', bg.counts, [5])

    sts = []
    for _, st in status.data.items():
        s = dict(st)
        s['state'] = bits_to_words(st['state'], K['SCS_MAX'])[0]
        s['calc_flag'] = bits_to_words(st['calc_flag'], K['SCB_MAX'])[0]
        s['flag'] = bits_to_words(st['flag'], K['SCF_MAX'])[0]
        del s['name']
        sts.append(s)
    relevant = [[i, v] for i, v in enumerate(status.relevant_bl) if v != K['BL_PC']]
    ctx['write_json']('misc_status', compact({
        'statuses': sts, 'relevant_bl_types': relevant, 'disabled': sorted([k, v] for k, v in disabled.items()),
        'scb_all': bits_to_words(status.scb_all, K['SCB_MAX'])[0], 'scb_battle': bits_to_words(status.scb_battle, K['SCB_MAX'])[0]}))
    ctx['write_json']('misc_size_fix', compact({'weapons': [[k, v] for k, v in size_fix.data.items()]}))
    ctx['write_json']('misc_attr_fix', {'attr_fix_table': attr.table})

    pets = []
    for _, p in pet.data.items():
        p = dict(p)
        p['evolution_data'] = [{'target_mob_id': e['target_mob_id'], 'requirements': e['requirements'].items()}
                               for _, e in p['evolution_data'].items()]
        pets.append(p)
    ctx['write_json']('misc_pet', compact({'pets': pets}))

    homs = []
    for _, h in hom.data.items():
        h = dict(h)
        h['skill_tree'] = [dict(e, need=e['need'].items()) for e in h['skill_tree']]
        homs.append(h)
    ctx['write_json']('misc_homunculus', compact({'homunculus': homs, 'exp': [[k, e['exp']] for k, e in hexp.data.items()]}))

    mercs = [dict(m, skill=m['skill'].items()) for _, m in merc.data.items()]
    ctx['write_json']('misc_mercenary', compact({'mercenaries': mercs}))
    elems = [dict(e, skill=[[mode, s] for mode, s in e['skill'].items()]) for _, e in elem.data.items()]
    ctx['write_json']('misc_elemental', compact({'elementals': elems}))
    ctx['write_json']('misc_castle', compact({'castles': castle.data.values()}))
    ctx['write_json']('misc_quest', compact({'quests': quest.data.values()}))
    achs = [dict(a, targets=sorted(a['targets'].items())) for _, a in ach.data.items()]
    ctx['write_json']('misc_achievement', compact({'achievements': achs, 'achievement_mobs': ach.achievement_mobs,
                                                   'feature_achievement': bool(w['conf']['feature_achievement']),
                                                   'levels': [[k, e['points']] for k, e in achl.data.items()]}))
    ctx['write_json']('misc_battleground', compact({'battlegrounds': bg.data.values()}))
    ctx['write_json']('misc_instance', compact({'instances': inst.data.values()}))
