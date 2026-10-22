// Dumps rAthena's map-server packet_db (id, length, parser, field offsets) for the configured PACKETVER as JSON.
// Mirrors clif.cpp packetdb_addpacket: every registration overwrites len/func and the offsets it passes; offsets a
// later, shorter registration does not pass keep their earlier values (packet_db is only memset once).
#include <cstdio>
#include <cstdint>
#include <cstddef>
#include <cstdarg>
#include <map/packets.hpp>
#include "parsers.inc"
struct Row { int len; const char* func; int pos[20]; bool used; };
static Row db[0x10000];
static void add(int cmd, int len, const char* func, va_list ap) {
    if (cmd <= 0 || cmd > 0xFFFF) return;
    db[cmd].used = true; db[cmd].len = len; db[cmd].func = func;
    for (int i = 0; i < 20; i++) { int off = va_arg(ap, int); if (off == 0) break; db[cmd].pos[i] = off; }
}
static void packetdb_addpacket(int cmd, int len, const char* func, ...) { va_list ap; va_start(ap, func); add(cmd, len, func, ap); va_end(ap); }
static void packetdb_addpacket(int cmd, int len, std::nullptr_t, ...) { va_list ap; va_start(ap, len); db[cmd].used = true; db[cmd].len = len; db[cmd].func = nullptr; va_end(ap); }
static void fill() {
#include <map/clif_packetdb.hpp>
#include <map/clif_shuffle.hpp>
}
int main() {
    fill();
    printf("{\"packetver\":%d,\"packets\":[\n", PACKETVER);
    bool first = true;
    for (int c = 0; c < 0x10000; c++) {
        if (!db[c].used) continue;
        printf("%s{\"id\":%d,\"len\":%d,\"func\":\"%s\",\"pos\":[", first ? "" : ",", c, db[c].len, db[c].func ? db[c].func : "");
        int n = 0; while (n < 20 && db[c].pos[n]) n++;
        for (int i = 0; i < n; i++) printf("%s%d", i ? "," : "", db[c].pos[i]);
        printf("]}\n"); first = false;
    }
    printf("]}\n");
    return 0;
}
