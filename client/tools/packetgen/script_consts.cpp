// Dumps every constant rAthena's script engine defines in source (script.cpp script_hardcoded_constants →
// script_constants.hpp) for PACKETVER 20211103 / renewal, as "name<TAB>value<TAB>param<TAB>deprecated" lines.
#include <cstdio>
#include <cstdint>
#include <common/cbasetypes.hpp>
#include <common/mmo.hpp>
#include <map/achievement.hpp>
#include <map/atcommand.hpp>
#include <map/battle.hpp>
#include <map/battleground.hpp>
#include <map/cashshop.hpp>
#include <map/channel.hpp>
#include <map/chat.hpp>
#include <map/chrif.hpp>
#include <map/clan.hpp>
#include <map/clif.hpp>
#include <map/date.hpp>
#include <map/elemental.hpp>
#include <map/guild.hpp>
#include <map/homunculus.hpp>
#include <map/instance.hpp>
#include <map/intif.hpp>
#include <map/itemdb.hpp>
#include <map/log.hpp>
#include <map/mail.hpp>
#include <map/map.hpp>
#include <map/mapreg.hpp>
#include <map/mercenary.hpp>
#include <map/mob.hpp>
#include <map/npc.hpp>
#include <map/party.hpp>
#include <map/path.hpp>
#include <map/pc.hpp>
#include <map/pc_groups.hpp>
#include <map/pet.hpp>
#include <map/quest.hpp>
#include <map/storage.hpp>
#include <map/script.hpp>
#include <map/skill.hpp>
#include <map/status.hpp>
#include <map/searchstore.hpp>
#include <map/navi.hpp>
#undef script_set_constant
static void my_set(const char* name, long long value, const char* cname, bool isparameter, bool deprecated) { printf("%s\t%lld\t%d\t%d\n", name, value, (int)isparameter, (int)deprecated); }
#define script_set_constant(name, value, isparameter, deprecated) my_set(name, value, nullptr, isparameter, deprecated)
#define script_set_constant_(name, value, cname, isparameter, deprecated) my_set(name, value, cname, isparameter, deprecated)
static void dump() {
#undef SCRIPT_CONSTANTS_HPP
#include <map/script_constants.hpp>
}
int main() { dump(); return 0; }
