#include "proto.h"

#include <string.h>

// What one complete line asks for.
static command parse(const char *line)
{
    command cmd = { .kind = CMD_UNKNOWN };
    if (strcmp(line, "PING") == 0) {
        cmd.kind = CMD_PING;
    }
    return cmd;
}

bool proto_feed(line_reader *r, char c, command *out)
{
    // A line ends at CR or LF, so the register's LF, CR+LF and a terminal's bare CR all work.
    if (c != '\r' && c != '\n') {
        if (r->len < PROTO_MAX_LINE) {
            r->buf[r->len++] = c;
        } else {
            r->overflow = true; // keep reading to the end of the line, then answer unknown
        }
        return false;
    }

    // Empty lines, such as the one between CR and LF, are skipped.
    bool complete = r->len > 0 || r->overflow;
    if (complete) {
        r->buf[r->len] = '\0';
        *out = r->overflow ? (command){ .kind = CMD_UNKNOWN } : parse(r->buf);
    }
    r->len = 0;
    r->overflow = false;
    return complete;
}
