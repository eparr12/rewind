// The counter's line protocol (HW-1), in plain C with no HAL calls, so it compiles and runs
// its tests on the desktop with gcc (HW-9).
#pragma once

#include <stdbool.h>
#include <stdint.h>

// The longest line kept. A longer one is read to its end, then answered as unknown.
#define PROTO_MAX_LINE 64

typedef enum {
    CMD_PING,    // are you there? The board answers PONG (HW-10).
    CMD_UNKNOWN, // anything else; the board answers ERR|UNKNOWN (HW-5).
} cmd_kind;

typedef struct {
    cmd_kind kind;
} command;

// Collects the bytes of one line. Start it zeroed: line_reader reader = { 0 };
typedef struct {
    char buf[PROTO_MAX_LINE + 1];
    uint8_t len;
    bool overflow;
} line_reader;

// Feeds one byte from the serial port. When the byte ends a line that holds a message, it
// writes the command to *out and returns true; otherwise it returns false.
bool proto_feed(line_reader *r, char c, command *out);
