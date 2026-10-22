"""
Job and character-progression databases (rAthena e985006, RENEWAL), pc.cpp pc_readdb and do_init_pc:
  PenaltyDatabase (level_penalty.yml), JobDatabase (job_stats.yml importing job_stats / job_aspd / job_exp /
  job_basepoints / job_outfits + import), SkillTreeDatabase (skill_tree.yml, Inherit), PlayerStatPointDatabase
  (statpoint.yml + computed levels), AttendanceDatabase (attendance.yml + Loki's import; the period dates are
  resolved when the module reads it, see src/db/jobs.rs), ReputationDatabase (reputation.yml).
Writes data/json/jobs_*.json.z for src/db/jobs.rs. The YAML framework, libstdc++ map ordering and the skill_db
(needed by the skill tree) come from skills.py.

`python3 jobs.py --gen-ids <rathena> <out.rs>` writes the e_job / e_mapid constants and the pc_jobid2mapid,
pc_mapid2jobid and job_name tables, extracted from mmo.hpp / map.hpp / pc.cpp.
"""
import importlib.util, math, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))


def _skills():
    """skills.py as one shared module (datagen loads each plugin separately)."""
    name = 'loki_datagen_skills'
    if name not in sys.modules:
        spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, 'skills.py'))
        m = importlib.util.module_from_spec(spec)
        sys.modules[name] = m
        spec.loader.exec_module(m)
    return sys.modules[name]


S = _skills()
UMap, YamlDatabase, node_exists, child, kids, is_seq, wrap = S.UMap, S.YamlDatabase, S.node_exists, S.child, S.kids, S.is_seq, S.wrap

MAX_LEVEL = 275
PARAM_MAX = 12
PARAM_POW = 6
PARAMETER_NAMES = ['Str', 'Agi', 'Vit', 'Int', 'Dex', 'Luk', 'Pow', 'Sta', 'Wis', 'Spl', 'Con', 'Crt']   # pc.hpp
MAX_WEAPON_TYPE = 24          # pc.hpp weapon_type
AMOTION_ZERO_ASPD, AMOTION_INTERVAL = 2000, 10   # status.hpp
PENALTY_NONE, PENALTY_MAX = 0, 5
MAX_SKILL_LEVEL = 13
INT64_MIN = -(1 << 63)
UINT64_MAX = (1 << 64) - 1


# ============================================================================================ enums from the headers

def c_enum(src, name, known=None):
    """The members of `enum name [: type] { ... };` with C's numbering; initialisers may be integers, members,
    #define names from `known`, or `A|B` of those."""
    known = dict(known or {})
    m = re.search(r'enum\s+%s\s*(?::\s*\w+)?\s*\{(.*?)\};' % re.escape(name), src, re.S)
    body = re.sub(r'//[^\n]*', '', m.group(1))
    body = re.sub(r'/\*.*?\*/', '', body, flags=re.S)
    out, v = {}, -1

    def ev(expr):
        total = 0
        for t in expr.split('|'):
            t = t.strip()
            if re.match(r'^(0x[0-9a-fA-F]+|\d+)$', t):
                total |= int(t, 0)
            elif t == 'UINT64_MAX':
                total |= UINT64_MAX
            elif t in out:
                total |= out[t]
            else:
                total |= known[t]
        return total

    for part in body.split(','):
        part = part.strip()
        if not part:
            continue
        if '=' in part:
            n, e = [x.strip() for x in part.split('=', 1)]
            v = ev(e)
        else:
            n = part
            v += 1
        out[n] = v
    return out


def defines(src, prefix):
    return {m.group(1): int(m.group(2), 0) for m in re.finditer(r'#define\s+(%s\w*)\s+(0x[0-9a-fA-F]+|\d+)' % prefix, src)}


def switch_cases(src, func_sig):
    """(case labels, return expression) groups of the first switch in the function whose signature starts with
    func_sig (pc.cpp pc_jobid2mapid / pc_mapid2jobid / job_name)."""
    i = src.index(func_sig)
    j = src.index('\n}\n', i)
    body = re.sub(r'//[^\n]*', '', src[i:j])
    groups, labels = [], []
    for m in re.finditer(r'case\s+(\w+)\s*:|default\s*:|return\s+([^;]+);', body):
        if m.group(1):
            labels.append(m.group(1))
        elif m.group(0).startswith('default'):
            labels.append('default')
        else:
            groups.append((labels, m.group(2).strip()))
            labels = []
    return groups


class Enums:
    def __init__(self, root):
        mmo = open(os.path.join(root, 'src/common/mmo.hpp'), encoding='latin-1').read()
        maph = open(os.path.join(root, 'src/map/map.hpp'), encoding='latin-1').read()
        self.pc_cpp = open(os.path.join(root, 'src/map/pc.cpp'), encoding='latin-1').read()
        self.job = c_enum(mmo, 'e_job')
        self.jobl = defines(maph, 'JOBL_')
        self.masks = defines(maph, 'MAPID_')
        self.mapid = c_enum(maph, 'e_mapid', dict(self.jobl, **self.masks))
        self.jobid2mapid = {}   # job id -> mapid; 'default' -> the default branch
        for labels, ret in switch_cases(self.pc_cpp, 'uint64 pc_jobid2mapid(uint16 b_class)'):
            val = UINT64_MAX if ret == '-1' else self.mapid[ret]
            for l in labels:
                self.jobid2mapid[l if l == 'default' else self.job[l]] = val

    def pc_jobid2mapid(self, job_id):
        return self.jobid2mapid.get(job_id & 0xFFFF, self.jobid2mapid['default'])

    def pcdb_checkid(self, class_):
        """pc.hpp pcdb_checkid_sub((uint32)class_)."""
        J = self.job
        c = class_ & 0xFFFFFFFF
        return (c < J['JOB_MAX_BASIC']
                or J['JOB_NOVICE_HIGH'] <= c <= J['JOB_DARK_COLLECTOR']
                or J['JOB_RUNE_KNIGHT'] <= c <= J['JOB_MECHANIC_T2']
                or J['JOB_BABY_RUNE_KNIGHT'] <= c <= J['JOB_BABY_MECHANIC2']
                or J['JOB_SUPER_NOVICE_E'] <= c <= J['JOB_SUPER_BABY_E']
                or J['JOB_KAGEROU'] <= c <= J['JOB_OBORO']
                or c == J['JOB_REBELLION'] or c == J['JOB_SUMMONER']
                or c == J['JOB_BABY_SUMMONER']
                or J['JOB_BABY_NINJA'] <= c <= J['JOB_BABY_REBELLION']
                or J['JOB_BABY_STAR_GLADIATOR2'] <= c <= J['JOB_BABY_STAR_EMPEROR2']
                or J['JOB_DRAGON_KNIGHT'] <= c <= J['JOB_TROUVERE']
                or J['JOB_WINDHAWK2'] <= c <= J['JOB_IMPERIAL_GUARD2']
                or J['JOB_SKY_EMPEROR'] <= c <= J['JOB_SPIRIT_HANDLER']
                or c == J['JOB_SKY_EMPEROR2'])

    # pc.hpp macros on a mapid
    def is_primary_fourth(self, c):
        M, F = self.mapid, self.masks['MAPID_FOURTHMASK']
        return M['MAPID_DRAGON_KNIGHT'] <= (c & F) <= M['MAPID_SHADOW_CROSS'] or M['MAPID_IMPERIAL_GUARD'] <= (c & F) <= M['MAPID_ABYSS_CHASER']

    def is_upper_expanded_second(self, c):
        M, K = self.mapid, self.masks
        return ((c & K['MAPID_FOURTHMASK']) == M['MAPID_HYPER_NOVICE'] or (c & K['MAPID_FOURTHMASK']) == M['MAPID_SKY_EMPEROR']
                or (c & K['MAPID_FOURTHMASK']) == M['MAPID_SOUL_ASCETIC'] or (c & K['MAPID_THIRDMASK']) == M['MAPID_NIGHT_WATCH']
                or (c & K['MAPID_THIRDMASK']) == M['MAPID_SHINKIROSHIRANUI'] or (c & K['MAPID_SECONDMASK']) == M['MAPID_SPIRIT_HANDLER'])

    def is_trait_job(self, c):
        return self.is_primary_fourth(c) or self.is_upper_expanded_second(c)


# ============================================================================================ PenaltyDatabase

class PenaltyDatabase(YamlDatabase):
    """pc.cpp PenaltyDatabase ("PENALTY_DB" v1): rate[difference + MAX_LEVEL - 1] per level difference."""
    type_, location = 'PENALTY_DB', 'db/level_penalty.yml'

    def parse_body_node(self, node):
        ok, t = self.as_string(node, 'Type')
        if not ok:
            return 0
        c = S.script_get_constant(self.ctx, 'PENALTY_' + t)
        if c is None:
            self.warn('Unknown penalty type "%s".' % t)
            return 0
        if c < PENALTY_NONE or c > PENALTY_MAX:
            self.warn('Invalid penalty type "%s".' % t)
            return 0
        pen = self.find(c)
        exists = pen is not None
        if not exists:
            pen = {'type': c, 'rate': [0xFFFF] * (MAX_LEVEL * 2 - 1)}
        if node_exists(node, 'LevelDifferences'):
            for _, ln in kids(child(node, 'LevelDifferences')):
                if not self.nodes_exist(ln, ['Difference', 'Rate']):
                    return 0
                ok, diff = self.as_int(ln, 'Difference', 32, True)
                if not ok:
                    return 0
                if abs(diff) > MAX_LEVEL:
                    self.warn('Level difference %d is bigger than maximum level %d.' % (diff, MAX_LEVEL))
                    return 0
                ok, rate = self.as_uint16_rate(ln, 'Rate')
                if not ok:
                    return 0
                if not 0 <= diff + MAX_LEVEL - 1 < len(pen['rate']):
                    self.warn('Difference %d indexes outside s_penalty::rate (undefined in rAthena), ignored.' % diff)
                    continue
                pen['rate'][diff + MAX_LEVEL - 1] = rate
        if not exists:
            self.put(c, pen)
        return 1

    def loading_finished(self):
        for _, pen in self.data.items():
            r, last = pen['rate'], 100
            for i in range(MAX_LEVEL - 1, len(r)):
                if r[i] == 0xFFFF:
                    r[i] = last
                else:
                    last = r[i]
            last = 100
            for i in range(MAX_LEVEL - 1, -1, -1):
                if r[i] == 0xFFFF:
                    r[i] = last
                else:
                    last = r[i]


# ============================================================================================ JobDatabase

class JobDatabase(YamlDatabase):
    """pc.cpp JobDatabase ("JOB_STATS" v4, minimum 2; TypesafeCachedYamlDatabase<uint16, s_job_info>)."""
    type_, version, min_version, location = 'JOB_STATS', 4, 2, 'db/job_stats.yml'

    def __init__(self, ctx, en, conf):
        super().__init__(ctx)
        self.en, self.conf = en, conf

    def const(self, name):
        return S.script_get_constant(self.ctx, name)

    def parse_body_node(self, node):
        if not node_exists(node, 'Jobs'):
            return 1
        for job_name, _ in kids(child(node, 'Jobs')):   # the true/false value is never read
            job_id = self.const('JOB_' + job_name)
            if job_id is None:
                self.warn('Job %s does not exist.' % job_name)
                return 0
            jid = wrap(job_id, 16, False)
            job = self.find(jid)
            exists = job is not None
            if not exists:
                job = {'job_id': jid, 'base_hp': [0] * MAX_LEVEL, 'base_sp': [0] * MAX_LEVEL, 'base_ap': [0] * MAX_LEVEL,
                       'hp_factor': 0, 'hp_increase': 0, 'sp_factor': 0, 'sp_increase': 0, 'ap_factor': 0,
                       'ap_increase': 0, 'max_weight_base': 0, 'job_bonus': [[0] * PARAM_MAX for _ in range(MAX_LEVEL)],
                       'aspd_base': [], 'base_exp': [0] * MAX_LEVEL, 'job_exp': [0] * MAX_LEVEL,
                       'max_base_level': 0, 'max_job_level': 0, 'max_param': [0] * PARAM_MAX,
                       'alternate_outfits': []}
            for key, field, bits, default in (('MaxWeight', 'max_weight_base', 32, 20000), ('HpFactor', 'hp_factor', 32, 0),
                                              ('HpIncrease', 'hp_increase', 32, 500), ('SpFactor', 'sp_factor', 32, 0),
                                              ('SpIncrease', 'sp_increase', 32, 100), ('ApFactor', 'ap_factor', 32, 0),
                                              ('ApIncrease', 'ap_increase', 32, 0)):
                if node_exists(node, key):
                    ok, v = self.as_int(node, key, bits, False)
                    if not ok:
                        return 0
                    job[field] = v
                elif not exists:
                    job[field] = default
            mx = MAX_WEAPON_TYPE + 1                       # RENEWAL: an extra column for shields
            def_aspd = AMOTION_ZERO_ASPD // AMOTION_INTERVAL   # RENEWAL: amotion / interval
            if node_exists(node, 'BaseASPD'):
                an = child(node, 'BaseASPD')
                if not exists:
                    job['aspd_base'] = [def_aspd] * mx
                for weapon, _ in kids(an):
                    c = self.const('W_' + weapon)
                    if c is None:
                        self.warn('Unknown weapon type %s specified for %s, skipping.' % (weapon, job_name))
                        continue
                    if c < 0 or c >= mx:
                        self.warn('Invalid weapon type %s specified for %s, skipping.' % (weapon, job_name))
                        continue
                    ok, aspd = self.as_int(an, weapon, 16, True)
                    if not ok:
                        return 0
                    job['aspd_base'][c] = aspd
            elif not exists:
                job['aspd_base'] = [def_aspd] * mx
            if node_exists(node, 'MaxStats'):
                sn = child(node, 'MaxStats')
                for stat, _ in kids(sn):
                    c = self.const('PARAM_' + stat)
                    if c is None:
                        self.warn('Unknown max stat %s specified for %s, skipping.' % (stat, job_name))
                        continue
                    if c < 0 or c >= PARAM_MAX:
                        self.warn('Invalid max stat %s specified for %s, skipping.' % (stat, job_name))
                        continue
                    ok, v = self.as_int(sn, stat, 16, False)
                    if not ok:
                        return 0
                    job['max_param'][c] = v
            for maxkey, maxfield, expkey, expfield in (('MaxBaseLevel', 'max_base_level', 'BaseExp', 'base_exp'),
                                                       ('MaxJobLevel', 'max_job_level', 'JobExp', 'job_exp')):
                if node_exists(node, maxkey):
                    ok, level = self.as_int(node, maxkey, 16, False)
                    if not ok:
                        return 0
                    if level == 0 or level > MAX_LEVEL:
                        self.warn('%s must be between 1~MAX_LEVEL for %s, capping to MAX_LEVEL.' % (maxkey, job_name))
                        level = MAX_LEVEL
                    job[maxfield] = level
                elif not exists:
                    job[maxfield] = MAX_LEVEL
                if node_exists(node, expkey):
                    for _, en in kids(child(node, expkey)):
                        ok, level = self.as_int(en, 'Level', 16, False)
                        if not ok:
                            return 0
                        if level > job[maxfield]:
                            continue
                        if level == 0 or level > MAX_LEVEL:
                            self.warn('Level must be between 1~MAX_LEVEL for %s.' % job_name)
                            return 0
                        if node_exists(en, 'Exp'):
                            ok, exp = self.as_int(en, 'Exp', 64, False)
                            if not ok:
                                return 0
                            job[expfield][level - 1] = exp
            if node_exists(node, 'BonusStats'):
                for _, ln in kids(child(node, 'BonusStats')):
                    ok, level = self.as_int(ln, 'Level', 16, False)
                    if not ok:
                        return 0
                    if level == 0 or level > MAX_LEVEL:
                        self.warn('Level must be between 1~MAX_LEVEL for %s.' % job_name)
                        return 0
                    for idx in range(PARAM_MAX):
                        if node_exists(ln, PARAMETER_NAMES[idx]):
                            ok, change = self.as_int(ln, PARAMETER_NAMES[idx], 16, True)
                            if not ok:
                                return 0
                            job['job_bonus'][level - 1][idx] = wrap(change, 16, False)   # std::array<uint16>
            for key, sub, field in (('BaseHp', 'Hp', 'base_hp'), ('BaseSp', 'Sp', 'base_sp'), ('BaseAp', 'Ap', 'base_ap')):   # HP_SP_TABLES
                if node_exists(node, key):
                    for _, pn in kids(child(node, key)):
                        ok, level = self.as_int(pn, 'Level', 16, False)
                        if not ok:
                            return 0
                        if level > job['max_base_level']:
                            continue
                        if level == 0 or level > MAX_LEVEL:
                            self.warn('Level must be between 1~MAX_LEVEL for %s.' % job_name)
                            return 0
                        if node_exists(pn, sub):
                            ok, pts = self.as_int(pn, sub, 32, False)
                            if not ok:
                                return 0
                            job[field][level - 1] = pts
            if node_exists(node, 'AlternateOutfits'):
                on = child(node, 'AlternateOutfits')
                for alt, _ in kids(on):
                    c = self.const('JOB_' + alt)
                    if c is None:
                        self.warn('Job %s does not exist.' % alt)
                        return 0
                    ok, active = self.as_bool(on, alt)
                    if not ok:
                        return 0
                    aid = wrap(c, 16, False)
                    if aid in job['alternate_outfits']:
                        if active:
                            self.warn('Job %s is already in the alternate outfit list.' % alt)
                        else:
                            job['alternate_outfits'].remove(aid)
                    elif active:
                        job['alternate_outfits'].append(aid)
                    else:
                        self.warn('Job %s is not in the alternate outfit list.' % alt)
            if not exists:
                self.put(jid, job)
        return 1

    # JobDatabase::calc_basehp / calc_basesp / calc_baseap (double arithmetic, RENEWAL)
    def calc_basehp(self, level, job):
        E, M = self.en, self.en.mapid
        mapid = E.pc_jobid2mapid(job['job_id'])
        base = 35.0
        base += math.floor(level * (job['hp_increase'] / 100.0))
        for i in range(2, level + 1):
            base += math.floor(((job['hp_factor'] / 100.0) * i) + 0.5)
        if (mapid & E.masks['MAPID_FIRSTMASK']) == M['MAPID_SUMMONER']:
            base += math.floor((base / 2) + 0.5)
        elif (mapid & E.masks['MAPID_SECONDMASK']) == M['MAPID_SUPER_NOVICE']:
            if level >= 99:
                base += 2000.0
            if level >= 150:
                base += 2000.0
        return wrap(int(base), 32, False)

    def calc_basesp(self, level, job):
        E, M = self.en, self.en.mapid
        mapid = E.pc_jobid2mapid(job['job_id'])
        base = 10.0
        base += math.floor(level * (job['sp_increase'] / 100.0))
        for i in range(2, level + 1):
            base += math.floor(((job['sp_factor'] / 100.0) * i) + 0.5)
        first = mapid & E.masks['MAPID_FIRSTMASK']
        if first == M['MAPID_NINJA']:
            if level >= 10:
                base -= 22.0
            else:
                base = 11.0 + 3.0 * level
        elif first == M['MAPID_GUNSLINGER']:
            if level >= 10:
                base -= 18.0
            else:
                base = 9.0 + 3.0 * level
        elif first == M['MAPID_SUMMONER']:
            base += math.floor((base / 2) + 0.5)
        return wrap(int(base), 32, False)

    def calc_baseap(self, level, job):
        base = 0.0
        if level >= 200:
            base = 200.0
        base += math.floor(level * (job['ap_increase'] / 100.0))
        for i in range(2, level + 1):
            base += math.floor(((job['ap_factor'] / 100.0) * i) + 0.5)
        return wrap(int(base), 32, False)

    def loading_finished(self):
        E, J, M, K, bc = self.en, self.en.job, self.en.mapid, self.en.masks, self.conf
        skip = {J[n] for n in ('JOB_WEDDING', 'JOB_XMAS', 'JOB_SUMMER', 'JOB_HANBOK', 'JOB_OKTOBERFEST', 'JOB_SUMMER2')}
        for job_id, job in self.data.items():
            if not E.pcdb_checkid(job_id) or job_id in skip:
                continue
            max_base, max_job = job['max_base_level'], job['max_job_level']
            if not max_base:
                self.warn('Class %d does not have a base exp table.' % job_id)
            if not max_job:
                self.warn('Class %d does not have a job exp table.' % job_id)
            for field, calc in (('base_hp', self.calc_basehp), ('base_sp', self.calc_basesp), ('base_ap', self.calc_baseap)):
                if not job[field]:
                    job[field] = [0] * max_base
                for j in range(max_base):
                    if job[field][j] == 0:
                        job[field][j] = calc(j + 1, job)
                del job[field][max_base:]   # erase(begin + maxBaseLv, end) when capacity (MAX_LEVEL) > maxBaseLv
            del job['job_bonus'][max_job:]
            for p in range(PARAM_MAX):
                current = 0
                for lv in range(max_job):
                    current = wrap(current + job['job_bonus'][lv][p], 16, True)   # int16 running total
                    job['job_bonus'][lv][p] = wrap(current, 16, False)
            c = E.pc_jobid2mapid(job_id)
            mx = bc['max_parameter']
            while True:
                if c & E.jobl['JOBL_BABY']:
                    mx = bc['max_baby_third_parameter'] if c & E.jobl['JOBL_THIRD'] else bc['max_baby_parameter']
                    break
                if (c & K['MAPID_FIRSTMASK']) == M['MAPID_SUMMONER']:
                    mx = bc['max_summoner_parameter']
                    break
                if (c & K['MAPID_SECONDMASK']) in (M['MAPID_KAGEROUOBORO'], M['MAPID_REBELLION']):
                    mx = bc['max_extended_parameter']
                    break
                if E.is_trait_job(c):
                    mx = bc['max_fourth_parameter']
                    break
                if c & E.jobl['JOBL_THIRD']:
                    if (c & E.jobl['JOBL_UPPER']) or E.is_primary_fourth(c):
                        mx = bc['max_third_trans_parameter']
                    else:
                        mx = bc['max_third_parameter']
                    break
                if c & E.jobl['JOBL_UPPER']:
                    mx = bc['max_trans_parameter']
                break
            for p in range(PARAM_POW):
                if job['max_param'][p] == 0:
                    job['max_param'][p] = wrap(mx, 16, False)
            mx = bc['max_trait_parameter'] if E.is_trait_job(c) else 0
            for p in range(PARAM_POW, PARAM_MAX):
                if job['max_param'][p] == 0:
                    job['max_param'][p] = wrap(mx, 16, False)

    def get_max_base_lv(self, job_id):
        j = self.find(job_id)
        return j['max_base_level'] if j else 0

    def get_max_job_lv(self, job_id):
        j = self.find(job_id)
        return j['max_job_level'] if j else 0


# ============================================================================================ SkillTreeDatabase

class SkillTreeDatabase(YamlDatabase):
    """pc.cpp SkillTreeDatabase ("SKILL_TREE_DB" v1). Entries are shared between a job and the jobs that inherit it
    (shared_ptr), which matters for the level capping in loadingFinished."""
    type_, location = 'SKILL_TREE_DB', 'db/skill_tree.yml'

    def __init__(self, ctx, en, sdb, jobdb):
        super().__init__(ctx)
        self.en, self.sdb, self.jobdb = en, sdb, jobdb

    def parse_body_node(self, node):
        ok, job_name = self.as_string(node, 'Job')
        if not ok:
            return 0
        c = S.script_get_constant(self.ctx, 'JOB_' + job_name)
        if c is None or not self.en.pcdb_checkid(c):
            self.warn('Invalid job %s.' % job_name)
            return 0
        job_id = wrap(c, 16, False)
        tree = self.find(job_id)
        exists = tree is not None
        if not exists:
            tree = {'inherit_job': [], 'skills': UMap()}
        if node_exists(node, 'Inherit'):
            inode = child(node, 'Inherit')
            for iname, _ in kids(inode):
                c = S.script_get_constant(self.ctx, 'JOB_' + iname)
                if c is None or not self.en.pcdb_checkid(c):
                    self.warn('Invalid job %s.' % iname)
                    return 0
                ok, active = self.as_bool(inode, iname)
                if not ok:
                    return 0
                ij = wrap(c, 16, False)
                if not active:
                    if exists and ij in tree['inherit_job']:
                        tree['inherit_job'].remove(ij)
                elif ij not in tree['inherit_job']:
                    tree['inherit_job'].append(ij)
        if node_exists(node, 'Tree'):
            for _, it in kids(child(node, 'Tree')):
                ok, skill_name = self.as_string(it, 'Name')
                if not ok:
                    return 0
                skill_id = self.sdb.name2id(skill_name)
                if skill_id == 0:
                    self.warn('Invalid skill name "%s".' % skill_name)
                    return 0
                if not self.sdb.get_index(skill_id):
                    self.warn('Unable to load skill %s into job %d\'s tree.' % (skill_name, job_id))
                    return 0
                entry = tree['skills'].get(skill_id)
                skill_exists = entry is not None
                if not skill_exists:
                    entry = {'skill_id': 0, 'max_lv': 0, 'baselv': 0, 'joblv': 0, 'need': UMap(), 'exclude_inherit': False}
                entry['skill_id'] = skill_id
                ok, max_lv = self.as_int(it, 'MaxLevel', 16, False)
                if not ok:
                    return 0
                if max_lv > MAX_SKILL_LEVEL:
                    self.warn('MaxLevel exceeds the maximum skill level of %d, skipping.' % MAX_SKILL_LEVEL)
                    return 0
                lv_max = self.sdb.get_max(skill_id)
                if max_lv > lv_max:
                    self.warn('Skill %s\'s level %d exceeds the skill\'s max level %d. Capping skill level.' % (skill_name, max_lv, lv_max))
                    max_lv = lv_max
                entry['max_lv'] = max_lv
                for key, field, getmax in (('BaseLevel', 'baselv', self.jobdb.get_max_base_lv),
                                           ('JobLevel', 'joblv', self.jobdb.get_max_job_lv)):
                    if node_exists(it, key):
                        ok, lv = self.as_int(it, key, 32, False)
                        if not ok:
                            return 0
                        m = getmax(job_id)
                        if lv > m:
                            self.warn('Skill %d\'s %s requirement %d exceeds job %s\'s max. Capping.' % (skill_id, key, lv, job_name))
                            lv = m
                        entry[field] = lv
                    elif not skill_exists:
                        entry[field] = 0
                if node_exists(it, 'Requires'):
                    for _, rn in kids(child(it, 'Requires')):
                        if not self.nodes_exist(rn, ['Name']):
                            return 0
                        ok, rname = self.as_string(rn, 'Name')
                        if not ok:
                            return 0
                        rid = self.sdb.name2id(rname)
                        if rid == 0:
                            self.warn('Invalid skill name "%s".' % rname)
                            return 0
                        ok, lv_req = self.as_int(rn, 'Level', 16, False)
                        if not ok:
                            return 0
                        if lv_req > MAX_SKILL_LEVEL:
                            self.warn('Level exceeds the maximum skill level of %d, skipping.' % MAX_SKILL_LEVEL)
                            return 0
                        m = self.sdb.get_max(rid)
                        if lv_req > m:
                            self.warn('Required skill %s\'s level %d exceeds the skill\'s max level %d. Capping skill level.' % (skill_name, lv_req, m))
                            lv_req = m
                        if lv_req == 0:
                            if entry['need'].erase(rid) == 0:
                                self.warn('Failed to erase %s, the skill doesn\'t exist in for job %s, skipping.' % (rname, job_name))
                            continue
                        entry['need'][rid] = lv_req
                if node_exists(it, 'Exclude'):
                    ok, ex = self.as_bool(it, 'Exclude')
                    if not ok:
                        return 0
                    entry['exclude_inherit'] = ex
                elif not skill_exists:
                    entry['exclude_inherit'] = False
                if not skill_exists:
                    tree['skills'].insert(skill_id, entry)
        if not exists:
            self.put(job_id, tree)
        return 1

    def loading_finished(self):
        trans2_of = {7: 4008, 8: 4009, 9: 4010, 10: 4011, 11: 4012, 12: 4013, 14: 4015, 15: 4016, 16: 4017, 17: 4018, 18: 4019, 19: 4020, 20: 4021}
        for job_id, data in self.data.items():
            if (4054 <= job_id <= 4059) or (4066 <= job_id <= 4072):
                for j2, th in trans2_of.items():
                    if j2 in data['inherit_job'] and th not in data['inherit_job']:
                        data['inherit_job'].append(th)
        job_tree = UMap()
        for job_id, data in self.data.items():
            if not data['inherit_job']:
                continue
            skill_tree = UMap()
            baselv_max = self.jobdb.get_max_base_lv(job_id)
            joblv_max = self.jobdb.get_max_job_lv(job_id)
            for inherit_job in data['inherit_job']:
                tree = self.find(inherit_job)
                if tree is None or not len(tree['skills']):
                    continue
                for sid, entry in tree['skills'].items():
                    if entry['exclude_inherit']:
                        continue
                    if sid in data['skills']:
                        continue
                    if sid in skill_tree:
                        skill_tree[sid] = entry     # replaced by the last inheritance
                    else:
                        skill_tree.insert(sid, entry)
                    skill = skill_tree[sid]
                    if skill['baselv'] > baselv_max:   # the entry is shared: the parent's is capped too
                        self.warn('SkillTreeDatabase: Skill %d base level requirement capped for job %d.' % (sid, job_id))
                        skill['baselv'] = baselv_max
                    if skill['joblv'] > joblv_max:
                        self.warn('SkillTreeDatabase: Skill %d job level requirement capped for job %d.' % (sid, job_id))
                        skill['joblv'] = joblv_max
            if len(skill_tree):
                job_tree.insert(job_id, skill_tree)
        if len(job_tree):
            for job_id, data in self.data.items():
                if job_id not in job_tree:
                    continue
                for sid, entry in job_tree[job_id].items():   # insert(first, last): existing keys are kept
                    data['skills'].insert(sid, entry)
        for job_id, data in self.data.items():
            for sid in [k for k, e in data['skills'].items() if e['max_lv'] == 0]:
                data['skills'].erase(sid)


# ============================================================================================ PlayerStatPointDatabase

class PlayerStatPointDatabase(YamlDatabase):
    """pc.cpp PlayerStatPointDatabase ("STATPOINT_DB" v2, minimum 1)."""
    type_, version, min_version, location = 'STATPOINT_DB', 2, 1, 'db/statpoint.yml'

    def __init__(self, ctx, conf, inter):
        super().__init__(ctx)
        self.conf, self.inter = conf, inter

    def parse_body_node(self, node):
        ok, level = self.as_int(node, 'Level', 16, False)
        if not ok:
            return 0
        if level == 0:
            self.warn('The minimum level is 1.')
            return 0
        if level > MAX_LEVEL:
            self.warn('Level %d exceeds maximum BaseLevel %d, skipping.' % (level, MAX_LEVEL))
            return 0
        entry = self.find(level)
        exists = entry is not None
        if not exists:
            if not self.nodes_exist(node, ['Points']):
                return 0
            entry = {'level': level, 'statpoints': 0, 'traitpoints': 0}
        if node_exists(node, 'Points'):
            ok, pts = self.as_int(node, 'Points', 32, False)
            if not ok:
                return 0
            entry['statpoints'] = pts
        if node_exists(node, 'TraitPoints'):
            ok, pts = self.as_int(node, 'TraitPoints', 32, False)
            if not ok:
                return 0
            entry['traitpoints'] = pts
        elif not exists:
            entry['traitpoints'] = 0
        if not exists:
            self.put(level, entry)
        return 1

    def loading_finished(self):
        trait_start_level = 200
        use_stat, use_trait = self.conf['use_statpoint_table'], self.conf['use_traitpoint_table']
        start = self.inter['start_status_points']
        one = self.find(1)
        if one is None:
            if use_stat:
                self.warn('Missing status points for Level 1')
            one = {'level': 1, 'statpoints': start, 'traitpoints': 0}
            self.put(1, one)
        elif use_stat:
            if one['statpoints'] != start:
                self.warn('Status points for Level 1 (=%d) do not match inter_athena.conf value (=%d).' % (one['statpoints'], start))
                one['statpoints'] = start
        else:
            one['statpoints'] = start
            one['traitpoints'] = 0
        last = one
        for level in range(2, MAX_LEVEL + 1):
            entry = self.find(level)
            exists = entry is not None
            if not exists:
                entry = {'level': level, 'statpoints': 0, 'traitpoints': 0}
                self.put(level, entry)
            if not use_stat or not exists:
                if use_stat:
                    self.warn('Missing status points for Level %d' % level)
                if level <= trait_start_level:
                    entry['statpoints'] = wrap(last['statpoints'] + ((level - 1 + 15) // 5), 32, False)
                else:
                    entry['statpoints'] = last['statpoints']
            if not use_trait or not exists:
                if use_trait and level > trait_start_level:
                    self.warn('Missing trait points for Level %d' % level)
                if level > trait_start_level:
                    entry['traitpoints'] = (level - trait_start_level) * 3 + (level - trait_start_level) // 5 * 4
                else:
                    entry['traitpoints'] = 0
            last = entry


# ============================================================================================ AttendanceDatabase

class AttendanceNodes(YamlDatabase):
    """pc.cpp AttendanceDatabase ("ATTENDANCE_DB" v1). parseBodyNode depends on the date it runs (a period whose End
    has passed is skipped, and that changes which later entries collide), so this records every Body node, in load
    order, with each value already read the way parseBodyNode reads it; src/db/jobs.rs replays parseBodyNode for a
    given date. `bad` marks a value rAthena fails to read (asUInt32/asUInt16 false, or an unknown item: `continue`)."""
    type_, location = 'ATTENDANCE_DB', 'db/attendance.yml'

    def __init__(self, ctx, items):
        super().__init__(ctx)
        self.items = items
        self.nodes = []

    def parse_body_node(self, node):
        ok, start = self.as_int(node, 'Start', 32, False)
        if not ok:
            return 0   # nothing happens before this failure: the node never counts
        n = {'start': start, 'end': None, 'rewards': None}
        if node_exists(node, 'End'):
            ok, end = self.as_int(node, 'End', 32, False)
            n['end'] = {'bad': not ok, 'value': end}
        if node_exists(node, 'Rewards'):
            rs = []
            for _, rn in kids(child(node, 'Rewards')):
                r = {'day': None, 'item': None, 'amount': None}
                ok, day = self.as_int(rn, 'Day', 32, False)
                r['day'] = {'bad': not ok, 'value': day}
                if node_exists(rn, 'ItemId'):
                    ok, iid = self.as_int(rn, 'ItemId', 32, False)
                    r['item'] = {'bad': not ok or not self.items.exists(iid), 'value': iid}
                if node_exists(rn, 'Amount'):
                    ok, amount = self.as_int(rn, 'Amount', 16, False)
                    r['amount'] = {'bad': not ok, 'value': amount}
                rs.append(r)
            n['rewards'] = rs
        self.nodes.append(n)
        return 1   # the real count depends on the date (src/db/jobs.rs)


# ============================================================================================ ReputationDatabase

class ReputationDatabase(YamlDatabase):
    """pc.cpp ReputationDatabase ("REPUTATION_DB" v1), map-server build (no MAP_GENERATOR: no Visibility;
    reputation_group.yml is only loaded by map-server-generator)."""
    type_, location = 'REPUTATION_DB', 'db/reputation.yml'

    def parse_body_node(self, node):
        ok, rid = self.as_int(node, 'Id', 64, True)
        if not ok:
            return 0
        rep = self.find(rid)
        exists = rep is not None
        if not exists:
            if not self.nodes_exist(node, ['Name', 'Variable']):
                return 0
            rep = {'id': rid, 'name': '', 'variable': '', 'minimum': 0, 'maximum': 0}
        if node_exists(node, 'Name'):
            ok, name = self.as_string(node, 'Name')
            if not ok:
                return 0
            rep['name'] = name
        if node_exists(node, 'Variable'):
            ok, var = self.as_string(node, 'Variable')
            if not ok:
                return 0
            if len(var) > 32:
                self.warn('Variable name "%s" exceeds maximum length 32.' % var)
                return 0
            rep['variable'] = var
        for key in ('Minimum', 'Maximum'):
            if node_exists(node, key):
                ok, v = self.as_int(node, key, 64, True)
                if not ok:
                    return 0
                rep[key.lower()] = v
            elif not exists:
                rep[key.lower()] = INT64_MIN
        if not exists:
            self.put(rid, rep)
        return 1


# ============================================================================================ driver

def load_all(ctx):
    if '_jobs' in ctx:
        return ctx['_jobs']
    sk = S.load_all(ctx)                        # do_init_skill runs before do_init_pc
    conf, inter = sk['conf'], S.inter_config(ctx)
    en = Enums(ctx['root'])
    penalty = PenaltyDatabase(ctx)              # pc_readdb: RENEWAL_DROP / RENEWAL_EXP
    penalty.load()
    jobdb = JobDatabase(ctx, en, conf)
    jobdb.load()
    tree = SkillTreeDatabase(ctx, en, sk['sdb'], jobdb)
    tree.load()
    statpoint = PlayerStatPointDatabase(ctx, conf, inter)
    statpoint.load()
    attendance = AttendanceNodes(ctx, sk['items'])
    attendance.load()
    reputation = ReputationDatabase(ctx)
    reputation.load()
    st = {'en': en, 'conf': conf, 'inter': inter, 'penalty': penalty, 'job': jobdb, 'tree': tree,
          'statpoint': statpoint, 'attendance': attendance, 'reputation': reputation}
    ctx['_jobs'] = st
    return st


def build(ctx):
    st = load_all(ctx)
    for name in ('penalty', 'job', 'tree', 'statpoint', 'attendance', 'reputation'):
        S.report(name, st[name])
    jobs = []
    for _, j in st['job'].data.items():   # job_db iteration order
        jobs.append(j)
    ctx['write_json']('jobs_db', {'jobs': jobs, 'loaded': st['job'].counts})
    trees = []
    for job_id, t in st['tree'].data.items():
        trees.append({'job_id': job_id, 'inherit_job': t['inherit_job'],
                      'skills': [{'skill_id': e['skill_id'], 'max_lv': e['max_lv'], 'baselv': e['baselv'], 'joblv': e['joblv'],
                                  'need': [[k, v] for k, v in e['need'].items()], 'exclude_inherit': e['exclude_inherit']}
                                 for _, e in t['skills'].items()]})
    ctx['write_json']('jobs_skill_tree', {'trees': trees, 'loaded': st['tree'].counts})
    sp = st['statpoint']
    ctx['write_json']('jobs_statpoint', {'levels': [[e['statpoints'], e['traitpoints']] for e in (sp.find(l) for l in range(1, MAX_LEVEL + 1))],
                                         'loaded': sp.counts})
    ctx['write_json']('jobs_misc', {
        'penalty': [{'type': p['type'], 'rate': p['rate']} for p in st['penalty'].data.values()],
        'attendance_nodes': st['attendance'].nodes,
        'reputation': [r for r in st['reputation'].data.values()],
        # (file, entries) as "Done reading" reports them; attendance counts nodes with a readable Start (the entries
        # rAthena keeps depend on the date, see src/db/jobs.rs)
        'loaded': st['penalty'].counts + st['attendance'].counts + st['reputation'].counts,
    })


# ============================================================================================ Rust constants

def gen_rust_ids(root, out):
    en = Enums(root)
    L = ['//! mmo.hpp `enum e_job`, map.hpp JOBL_* / MAPID_* and pc.cpp pc_jobid2mapid / pc_mapid2jobid / job_name, generated',
         '//! from rAthena e985006 by `python3 tools/datagen_mods/jobs.py --gen-ids <rathena> src/db/jobs/ids.rs`. Do not edit.',
         '', '#![allow(dead_code)]', '']
    for n, v in en.job.items():
        L.append('pub const %s: i32 = %d;' % (n, v))
    L.append('')
    for n, v in list(en.jobl.items()) + list(en.masks.items()):
        L.append('pub const %s: u64 = 0x%x;' % (n, v))
    for n, v in en.mapid.items():
        L.append('pub const %s: u64 = 0x%x;' % (n, v))
    L += ['', '/// pc.cpp pc_jobid2mapid (an unknown class gives -1 as uint64: every bit set).',
          'pub fn pc_jobid2mapid(b_class: u16) -> u64 {', '    match b_class as i32 {']
    for labels, ret in switch_cases(en.pc_cpp, 'uint64 pc_jobid2mapid(uint16 b_class)'):
        pats = [l for l in labels if l != 'default']
        val = 'u64::MAX' if ret == '-1' else ret
        if pats:
            L.append('        %s => %s,' % (' | '.join(pats), val))
        if 'default' in labels:
            L.append('        _ => %s,' % val)
    L += ['    }', '}', '', '/// pc.cpp pc_mapid2jobid (sex: 1 male, 0 female; -1 when there is no such class).',
          'pub fn pc_mapid2jobid(class_: u64, sex: i32) -> i32 {', '    match class_ {']
    for labels, ret in switch_cases(en.pc_cpp, 'int32 pc_mapid2jobid(uint64 class_, int32 sex)'):
        pats = [l for l in labels if l != 'default']
        m = re.match(r'sex\s*\?\s*(\w+)\s*:\s*(\w+)', ret)
        val = 'if sex != 0 { %s } else { %s }' % (m.group(1), m.group(2)) if m else ret
        if pats:
            L.append('        %s => %s,' % (' | '.join(pats), val))
        if 'default' in labels:
            L.append('        _ => %s,' % val)
    L += ['    }', '}', '', '/// pc.cpp job_name: the map_msg.conf message number of the class name.',
          'pub fn job_name_msg(class_: i32) -> u16 {', '    match class_ {']
    for labels, ret in switch_cases(en.pc_cpp, 'const char* job_name(int32 class_)'):
        m = re.match(r'msg_txt\(\s*nullptr\s*,\s*(.*)\)$', ret)
        expr = m.group(1)
        pats = [l for l in labels if l != 'default']
        if pats:
            if 'class_' in expr:
                for l in pats:   # evaluate e.g. `550 - JOB_NOVICE+class_` for each class
                    e = expr.replace('class_', str(en.job[l]))
                    e = re.sub(r'JOB_\w+', lambda mm: str(en.job[mm.group(0)]), e)
                    L.append('        %s => %d,' % (l, eval(e)))
            else:
                L.append('        %s => %s,' % (' | '.join(pats), expr.strip()))
        if 'default' in labels:
            L.append('        _ => %s,' % expr.strip())
    L += ['    }', '}']
    open(out, 'w').write('\n'.join(L) + '\n')


if __name__ == '__main__' and len(sys.argv) == 4 and sys.argv[1] == '--gen-ids':
    gen_rust_ids(sys.argv[2], sys.argv[3])
