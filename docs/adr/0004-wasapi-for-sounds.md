# ADR-0004: Play sounds through WASAPI, one player per sound

- **Status:** Accepted
- **Date:** 2026-10-23
- **Author:** Ethan

## Context

Rewind plays five short WAV clips (UI-7), and they overlap: a scan beep over the register chime, and from Phase 4 the Can't Decide spinner's ticks, about ten a second, over whatever else is playing. Build Plan step 1.4 chose NAudio with one output per sound. The first version gave each sound its own NAudio `WaveOut`, disposed in `PlaybackStopped`, as wall sheet R2 shows.

`WaveOut` uses WinMM's waveOut functions. Microsoft calls Waveform Audio a legacy feature, superseded by WASAPI, and recommends WASAPI for new code.

While closing out Phase 1, throwaway stress tests found that `WaveOut` crashes the whole process when many sounds overlap. The crash is an access violation inside WinMM's driver, `wdmaud.drv`, so no exception handler can catch it. Each test process started 150 ms silent clips together from thread-pool threads, round after round:

- 16 clips at once crashed 5 of 5 processes, with simultaneous starts and with staggered ones.
- 2 clips at once, the most Phase 1 ever plays, never crashed in 125 tries.
- Closing the WaveOuts one at a time behind a lock still crashed 2 of 5 processes at 32 clips. Serializing their opens too stopped the crashes, but one run stalled.
- At the Phase 4 cadence below, `WaveOut` didn't crash in 5 runs either. Its crash needs more overlap than the spinner makes.

Since step 1.5, every scan also plays a sound from the serial reader's thread, so a crash in the sound player takes the scanner down with it.

## Decision

`NAudioPlayer` plays each sound on its own NAudio `WasapiPlayer`, in shared mode.

- `Play` hands the work to the thread pool and returns at once. On the desktop, opening a player takes about 60 ms while the speakers are awake, and about 600 ms once they've been quiet long enough to go to sleep. Neither the renderer nor the serial reader should wait for that. Two sounds started within a few milliseconds of each other may therefore begin in either order, which `ISoundPlayer` says.
- Each player is built for one sound, from whichever speakers are the default at that moment, and disposes itself and its reader in `PlaybackStopped`.
- The code asks for shared mode by name: an exclusive player would refuse the second of two overlapping sounds.

## Alternatives

- **Keep `WaveOut`, behind a lock.** Serializing its closes didn't stop the crashes. Serializing its opens too did, but one run stalled, and a stall inside the lock would stall every caller, the serial reader among them.
- **NAudio's `WasapiOut`.** It survived the same stress tests, but NAudio 3.3 marks it obsolete in favor of `WasapiPlayer`.
- **One long-lived output fed by a `MixingSampleProvider`**, with the five clips converted to one format at load time. This is the other textbook NAudio design. It opens one device instead of one per sound, so a sound starts within one buffer period instead of waiting for a new player to open, and an output that stays open keeps the speakers awake. But a long-lived output has to handle speakers being unplugged, plugged back in or switched, which a player per sound gets for free (GEN-7, HW-4). It waits until a player per sound crashes, fails the spinner's cadence, or the clerk hears the wait for a beep, which Build Plan 3.3 checks on the register.

## Consequences

- The stress tests that crashed `WaveOut` passed with `WasapiPlayer`: 32 clips at once for 10 rounds, in 5 processes with simultaneous starts and 5 with staggered ones, with no crash or stall.
- The Phase 4 cadence passed too. It plays ten ticks a second for ten seconds from a UI thread, a chime at 3 s and two scan beeps at 6 s from pool threads, using silent copies of the real clips. In 5 runs, all 103 sounds finished and none failed, and `Play` returned in under 2 ms.
- Speakers unplugged and plugged back in work without a restart, since each sound opens the current default device.
- After 15 seconds of quiet or more, the desktop's speakers had gone to sleep, and opening a player took about 600 ms, nearly all of it waking the device. `WaveOut` took as long, so this didn't come with WASAPI. Playing silence at startup can't help: the speakers are asleep again well before the first scan. With one silent stream held open, a new player opened in about 80 ms. A first beep that late after a quiet stretch could read as a missed scan, so Build Plan 3.3 measures it on the register, whose speakers may sleep differently, and moves to the mixer design if the clerk can hear the wait.
- The harness can't hear any of this, so check by ear on the counter (TEST-9) how soon the beep follows a scan after a quiet stretch, and that overlapping sounds all play.
- Wall sheet R2 still shows one `WaveOut` per sound, with a warning about NAudio 3 renaming `WaveOutEvent`. It predates this decision. Build Plan 1.4's "one output per sound" still holds.
- If a player per sound ever crashes, fails the cadence or makes the clerk wait for a beep, the mixer design is next, in a new ADR.
