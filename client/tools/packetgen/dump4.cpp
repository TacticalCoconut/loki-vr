#include <cstdio>
#include <map/packets.hpp>
int main(){ printf("{\"sizes\":[\n"); bool first=true;
printf("%s{\"name\":\"packet_itemlist_equip\",\"id\":2873,\"dyn\":true,\"size\":%d}\n", first?"":",", (int)sizeof(packet_itemlist_equip)); first=false;
printf("%s{\"name\":\"packet_itemlist_normal\",\"id\":2825,\"dyn\":true,\"size\":%d}\n", first?"":",", (int)sizeof(packet_itemlist_normal)); first=false;
printf("%s{\"name\":\"PACKET_ZC_SPRITE_CHANGE\",\"id\":471,\"dyn\":false,\"size\":%d}\n", first?"":",", (int)sizeof(PACKET_ZC_SPRITE_CHANGE)); first=false;
printf("%s{\"name\":\"PACKET_ZC_USE_ITEM_ACK\",\"id\":456,\"dyn\":false,\"size\":%d}\n", first?"":",", (int)sizeof(PACKET_ZC_USE_ITEM_ACK)); first=false;
printf("%s{\"name\":\"PACKET_ZC_UPDATE_ITEM_FROM_BUYING_STORE\",\"id\":2534,\"dyn\":false,\"size\":%d}\n", first?"":",", (int)sizeof(PACKET_ZC_UPDATE_ITEM_FROM_BUYING_STORE)); first=false;
printf("%s{\"name\":\"packet_roulette_open_ack\",\"id\":2586,\"dyn\":false,\"size\":%d}\n", first?"":",", (int)sizeof(packet_roulette_open_ack)); first=false;
printf("%s{\"name\":\"packet_roulette_info_ack\",\"id\":2588,\"dyn\":true,\"size\":%d}\n", first?"":",", (int)sizeof(packet_roulette_info_ack)); first=false;
printf("%s{\"name\":\"packet_roulette_itemrecv_ack\",\"id\":2594,\"dyn\":false,\"size\":%d}\n", first?"":",", (int)sizeof(packet_roulette_itemrecv_ack)); first=false;
printf("%s{\"name\":\"packet_roulette_generate_ack\",\"id\":2592,\"dyn\":false,\"size\":%d}\n", first?"":",", (int)sizeof(packet_roulette_generate_ack)); first=false;
printf("]}\n"); return 0; }