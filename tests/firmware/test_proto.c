// Tests for the counter's line protocol, built and run on the desktop with gcc (HW-9, TEST-8).
// Each test feeds bytes the way they arrive from the register or a serial terminal.
#include <stdio.h>
#include <string.h>

#include "proto.h"

#define MAX_COMMANDS 8

// Every command one test's bytes produced.
typedef struct {
    int count;
    command commands[MAX_COMMANDS];
} fed;

static int failures;

// Reports a failed check with its line number, then carries on with the next one.
#define CHECK(condition)                                                          \
    do {                                                                          \
        if (!(condition)) {                                                       \
            printf("%s:%d: check failed: %s\n", __FILE__, __LINE__, #condition); \
            failures++;                                                           \
        }                                                                         \
    } while (0)

// Feeds text to a new reader one byte at a time, as the UART interrupt does.
static fed feed(const char *text)
{
    line_reader reader = { 0 };
    fed result = { 0 };
    for (const char *c = text; *c != '\0'; c++) {
        command cmd;
        if (proto_feed(&reader, *c, &cmd)) {
            if (result.count < MAX_COMMANDS) {
                result.commands[result.count] = cmd;
            }
            result.count++;
        }
    }
    return result;
}

static void proto_feed_PingWithCrLf_GivesOnePing(void)
{
    fed result = feed("PING\r\n");

    CHECK(result.count == 1);
    CHECK(result.commands[0].kind == CMD_PING);
}

// PuTTY's Enter key sends a bare CR.
static void proto_feed_PingWithCr_GivesPing(void)
{
    fed result = feed("PING\r");

    CHECK(result.count == 1);
    CHECK(result.commands[0].kind == CMD_PING);
}

static void proto_feed_UnknownMessage_GivesUnknown(void)
{
    fed result = feed("HELLO\n");

    CHECK(result.count == 1);
    CHECK(result.commands[0].kind == CMD_UNKNOWN);
}

static void proto_feed_BlankLines_GiveNothing(void)
{
    fed result = feed("\n\r\n\r");

    CHECK(result.count == 0);
}

// A line too long to keep is answered once, as unknown, and the next line still reads.
static void proto_feed_LineTooLong_GivesUnknownThenReadsOn(void)
{
    char text[256];
    memset(text, 'A', 200);
    strcpy(text + 200, "\nPING\n");

    fed result = feed(text);

    CHECK(result.count == 2);
    CHECK(result.commands[0].kind == CMD_UNKNOWN);
    CHECK(result.commands[1].kind == CMD_PING);
}

// Every test is static, so a test missing from this list fails the build:
// -Wall warns about an unused static function, and -Werror makes that an error.
int main(void)
{
    proto_feed_PingWithCrLf_GivesOnePing();
    proto_feed_PingWithCr_GivesPing();
    proto_feed_UnknownMessage_GivesUnknown();
    proto_feed_BlankLines_GiveNothing();
    proto_feed_LineTooLong_GivesUnknownThenReadsOn();

    if (failures > 0) {
        printf("%d firmware check(s) failed\n", failures);
        return 1;
    }
    printf("Firmware tests passed\n");
    return 0;
}
