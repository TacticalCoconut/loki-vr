// Dumps rAthena's map-server packet table (id, length, parser) for the configured PACKETVER as JSON.
#include <cstdio>
#include <cstdint>
#include <cstddef>
#include <cstdarg>
#include <map/packets.hpp>
#include "parsers.inc"
struct Row { int cmd; int len; const char* func; };
static Row rows[8192]; static int nrows = 0;
static void packetdb_addpacket(int cmd, int len, const char* func, ...) { rows[nrows++] = Row{ cmd, len, func }; }
static void packetdb_addpacket(int cmd, int len, std::nullptr_t, ...) { rows[nrows++] = Row{ cmd, len, nullptr }; }
static void fill() {
#include <map/clif_packetdb.hpp>
#include <map/clif_shuffle.hpp>
}
int main() {
    fill();
    printf("{\"packetver\":%d,\"count\":%d,\"packets\":[\n", PACKETVER, nrows);
    for (int i = 0; i < nrows; i++) printf("%s{\"id\":%d,\"len\":%d,\"func\":\"%s\"}\n", i ? "," : "", rows[i].cmd, rows[i].len, rows[i].func ? rows[i].func : "");
    printf("]}\n");
    return 0;
}
