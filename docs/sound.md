# Sound packets

One handler receives VSND, VSMI, VSPS and VSBP. The VSND Enable flag starts and stops the sound streams: the meters, SMXR, SMXI, BLCK, GRPS, TRCK, SNDS, SEVT, SDCA and SDCU. Fields are little-endian unless a table says otherwise. Name fields are char[128] unless a table says otherwise.

## VSND

Sound viewer settings. The server stores the filters and sets the flags.

- Direction: C->S
- Version: 3

| Offset | Size | Type      | Name       | Meaning                                                                    |
| ------ | ---- | --------- | ---------- | -------------------------------------------------------------------------- |
| 0      | 128  | char[128] | Filter1    | Filter 1.                                                                  |
| 128    | 128  | char[128] | Filter2    | Filter 2.                                                                  |
| 256    | 8    | u64 LE    | Option64   | Bit mask. Not defined.                                                     |
| 264    | 4    | u32 LE    | Option32   | Default state ID or hash. The server uses it when it finds no state value. |
| 268    | 1    | u8        | Enable     | 1 starts the sound packets. 0 stops them.                                  |
| 269    | 1    | u8        | Visualizer | 1 starts the sound debug visualizer.                                       |
| 270    | 1    | u8        | Option8    | Not defined.                                                               |

## VSMI

Sets a mix graph input. The server hashes the name (MurmurHash2 of the lowercase text, seed `0x31415926`) and sets the input to Value.

- Direction: C->S
- Version: 3

| Offset | Size | Type      | Name  | Meaning                        |
| ------ | ---- | --------- | ----- | ------------------------------ |
| 0      | 128  | char[128] | Name  | Mix input name.                |
| 128    | 4    | f32 LE    | Value | Value.                         |
| 132    | 4    | u32 LE    | Extra | The server ignores this field. |

## VSPS

Plays a sound event with no source entity.

- Direction: C->S
- Version: 3

| Offset | Size | Type      | Name | Meaning           |
| ------ | ---- | --------- | ---- | ----------------- |
| 0      | 128  | char[128] | Name | Sound event name. |

## VSBP

The server accepts VSBP and ignores its payload. A client SHOULD send an empty payload.

- Direction: C->S
- Version: 3

## SVSM, SVDM and SMVM

Three sets of level meters. The server sends them each mix frame.

- Direction: S->C
- Version: 3

Contents of each packet:

- SVSM: `Main Output` first, then one meter for each submix.
- SVDM: one meter for each enabled audio meter of the mix graph.
- SMVM: one meter for each voice mix graph, named after its sound event.

| Offset | Size        | Type   | Name    | Meaning                                                                             |
| ------ | ----------- | ------ | ------- | ----------------------------------------------------------------------------------- |
| 0      | 4           | u32 LE | Count   | Number of meters. The server clamps it to 64 for SVSM and SVDM and to 128 for SMVM. |
| 4      | 152 x Count | entry  | Entries | Meters.                                                                             |

Meter (152 bytes):

| Offset | Size | Type       | Name   | Meaning                                                                                                                                                                                                                                |
| ------ | ---- | ---------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 128  | char[128]  | Name   | `Main Output`, the submix name, the display name of the audio meter, or the sound event name (`<EMPTY>` when it has none).                                                                                                             |
| 128    | 16   | f32 LE x 4 | Levels | Levels in dB, 10 x log10 of the power, with a floor of -90. For `Main Output`: left RMS, right RMS, left peak, right peak. The RMS level averages over 300 ms. The peak level jumps to the block maximum and then decays over 2000 ms. |
| 144    | 4    | u32 LE     | Value  | Submix: the solo name hash of the submix. SVDM and SMVM: the MurmurHash2 of the lowercase name ([VSMI](#vsmi)). `Main Output`: 0.                                                                                                      |
| 148    | 4    | u32 LE     | Kind   | Number of channels, 2 or fewer.                                                                                                                                                                                                        |

## SMXR

The mixgroup state.

- Direction: S->C
- Version: 3

| Offset        | Size       | Type      | Name    | Meaning                              |
| ------------- | ---------- | --------- | ------- | ------------------------------------ |
| 0             | 4          | u32 LE    | Count   | Number of mixgroups, 144 or fewer.   |
| 4             | 56         | bytes[56] | Unset   | The server does not set these bytes. |
| 60            | 56 x Count | entry     | Entries | Mixgroups.                           |
| after Entries | var        | bytes     | Symbols | Symbol data. Not defined.            |

Mixgroup (56 bytes). The values include the factors of the parent group:

| Offset | Size | Type   | Name          | Meaning                                                                                             |
| ------ | ---- | ------ | ------------- | --------------------------------------------------------------------------------------------------- |
| 0      | 4    | u32 LE | NameSymbol    | Symbol of the mixgroup name.                                                                        |
| 4      | 4    | f32 LE | Volume        | Final volume: the mixer volume, the master layer volume, the mute and solo factors and DuckGain.    |
| 8      | 4    | f32 LE | Level         | Mixer level, multiplied by `snd_mixer_master_level`.                                                |
| 12     | 4    | f32 LE | Dsp           | Mixer DSP amount, multiplied by `snd_mixer_master_dsp`.                                             |
| 16     | 4    | f32 LE | Solo          | Solo amount, 0 to 1. 0 unless a solo is active.                                                     |
| 20     | 4    | f32 LE | Mute          | Always 0.                                                                                           |
| 24     | 4    | f32 LE | DuckGain      | Duck gain when the group is ducked, else 1.0. Always 1.0 when `snd_disable_mixer_duck` is on.       |
| 28     | 4    | f32 LE | LayerVolume   | Master mix layer volume.                                                                            |
| 32     | 4    | f32 LE | LayerLevel    | Master mix layer level.                                                                             |
| 36     | 4    | f32 LE | LayerDsp      | Master mix layer DSP amount.                                                                        |
| 40     | 4    | f32 LE | LayerSolo     | Master mix layer solo, 0 to 1.                                                                      |
| 44     | 4    | f32 LE | LayerMute     | Master mix layer mute, 0 to 1.                                                                      |
| 48     | 4    | f32 LE | TotalVolume   | Sum of the voice volumes in a group that causes ducking. The server resets it at each mixer update. |
| 52     | 4    | f32 LE | TriggerVolume | Sum of the voice volumes, or their maximum when the group sets `max_trigger_amounts`.               |

## SMXI

The mix graph inputs of the current mix graph. The server sends it each mix update while the graph has its debug flag. The graph gets that flag while the VSND Enable flag is 1, or while another sound debug listener in the process is attached.

- Direction: S->C
- Version: 3

The payload is a u32 LE Count, then Count entries.

Input (136 bytes):

| Offset | Size | Type             | Name   | Meaning                                                                      |
| ------ | ---- | ---------------- | ------ | ---------------------------------------------------------------------------- |
| 0      | 128  | char[128]        | Name   | Input name.                                                                  |
| 128    | 4    | f32 LE           | Value  | The input value when the input is a float, else 0.                           |
| 132    | 4    | u32 LE or i32 LE | Value2 | The input value when the input is an integer or a string token hash, else 0. |

The packet does not carry the input type.

## BLCK

The sound event blocks: the match entries of the sound operator system. It lists each used entry of a fixed pool of 64. Length counts one more entry than Count ([Inaccurate lengths and garbage bytes](errors.md#inaccurate-lengths-and-garbage-bytes)).

- Direction: S->C
- Version: 3

The payload is a u32 LE Count, then Count entries with no padding.

Block (146 bytes):

| Offset | Size | Type      | Name           | Meaning                                                                    |
| ------ | ---- | --------- | -------------- | -------------------------------------------------------------------------- |
| 0      | 1    | u8        | Active         | 1 when the entry is active.                                                |
| 1      | 128  | char[128] | Name           | Event name to match, or `---invalid event---` when the entry has no event. |
| 129    | 1    | u8        | MatchSubString | 1 when Name matches as a substring.                                        |
| 130    | 4    | i32 LE    | EventType      | Event type to match.                                                       |
| 134    | 4    | i32 LE    | EntityIndex    | Entity index to match.                                                     |
| 138    | 4    | f32 LE    | Duration       | Duration of the block.                                                     |
| 142    | 4    | f32 LE    | Elapsed        | Time since the block started, when the block is timed. Else 0.             |

## GRPS

The sound group counts. It lists branch 0 of each group, and each other branch while it is active. Length counts one more entry than Count ([Inaccurate lengths and garbage bytes](errors.md#inaccurate-lengths-and-garbage-bytes)).

- Direction: S->C
- Version: 3

The payload is a u32 LE Count, then Count entries.

Group (136 bytes):

| Offset | Size | Type      | Name        | Meaning                                                                                                                                                        |
| ------ | ---- | --------- | ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 128  | char[128] | Name        | `<group name>:<branch index>`.                                                                                                                                 |
| 128    | 4    | u32 LE    | Count       | Number of members of the branch.                                                                                                                               |
| 132    | 4    | u32 LE    | NullMembers | Number of members whose sound event stopped but whose hold time has not run out. These members still count in Count and toward the member limit of the branch. |

## TRCK

The sound track list: one entry for each track of the sound operator system. Length counts one more entry than Count ([Inaccurate lengths and garbage bytes](errors.md#inaccurate-lengths-and-garbage-bytes)).

- Direction: S->C
- Version: 3

| Offset | Size        | Type   | Name    | Meaning           |
| ------ | ----------- | ------ | ------- | ----------------- |
| 0      | 4           | u32 LE | Count   | Number of tracks. |
| 4      | 198 x Count | entry  | Entries | Tracks.           |

The server has room for 16 tracks. A client SHOULD treat a Count above 16 as invalid.

Track (198 bytes):

| Offset | Size | Type      | Name               | Meaning                                                                 |
| ------ | ---- | --------- | ------------------ | ----------------------------------------------------------------------- |
| 0      | 64   | char[64]  | TrackName          | Track name, or `---empty---`.                                           |
| 64     | 128  | char[128] | EventName          | Name of the sound event that holds the track, or `---invalid event---`. |
| 192    | 4    | f32 LE    | Priority           | Input priority of that event.                                           |
| 196    | 1    | u8        | PriorityOverride   | The `priority_override` setting of that event.                          |
| 197    | 1    | u8        | BlockEqualPriority | The `block_equal_priority` setting of that event.                       |

## SDCA

The full wave data cache list and the cache event history. The server sends SDCA when the VSND Enable flag changes from 0 to 1. While Enable is 1, it also sends SDCA at a sound system update when the cache allocated or freed memory since the last SDCA.

- Direction: S->C
- Version: 3

| Offset         | Size         | Type    | Name     | Meaning                      |
| -------------- | ------------ | ------- | -------- | ---------------------------- |
| 0              | 4            | u32 BE  | CountA   | Number of A entries.         |
| 4              | 4            | u32 BE  | CountB   | Number of B entries.         |
| 8              | 160 x CountA | entry A | EntriesA | Cache entries and free gaps. |
| after EntriesA | 138 x CountB | entry B | EntriesB | Cache event history.         |

Entry A (160 bytes). One entry for each cache entry, sorted by address, with entries for the free gaps between them:

| Offset | Size | Type      | Name      | Meaning                                                                  |
| ------ | ---- | --------- | --------- | ------------------------------------------------------------------------ |
| 0      | 8    | u64 BE    | Address   | Address of the wave data in the cache arena.                             |
| 8      | 8    | u64 BE    | Size      | Size of the wave data in bytes. For a gap, the gap size.                 |
| 16     | 4    | u32 BE    | LockCount | Lock count. 0xFFFFFFFF when the handle is not valid. 0 for a gap.        |
| 20     | 4    | i32 BE    | Priority  | Cache priority. 0 for a gap.                                             |
| 24     | 8    | f64 LE    | LoadTime  | Sound system time when the load finished. 0 before that. -1.0 for a gap. |
| 32     | 128  | char[128] | Name      | Sound file name, or `Unknown Name`. All zero for a gap.                  |

Entry B (138 bytes). The history holds the last 201 events or fewer. The server never clears it, so each SDCA sends the whole history again.

| Offset | Size | Type      | Name | Meaning                                                                                            |
| ------ | ---- | --------- | ---- | -------------------------------------------------------------------------------------------------- |
| 0      | 2    | u16 BE    | Kind | 0 = the server freed the wave data. 1 = a sound was requested while its data was not loaded.       |
| 2      | 8    | f64 LE    | Time | Sound system time.                                                                                 |
| 10     | 128  | char[128] | Name | Sound file name. For kind 1 with no file name, byte 0 is 0x0A and the other bytes are not defined. |

## SDCU

Updates to the rows of the last SDCA. The server sends SDCU only while the VSND Enable flag is 1. It sends SDCU at a sound system update when the lock count or the priority of a cache entry changed since the last SDCA or SDCU. It never sends SDCU in the same update as SDCA. The receiver updates the row with the same Address.

- Direction: S->C
- Version: 3

The payload is a u32 BE Count, then Count entries.

Update (16 bytes):

| Offset | Size | Type   | Name      | Meaning                     |
| ------ | ---- | ------ | --------- | --------------------------- |
| 0      | 8    | u64 BE | Address   | Address of an SDCA entry A. |
| 8      | 4    | u32 BE | LockCount | New lock count.             |
| 12     | 4    | i32 BE | Priority  | New cache priority.         |

## SNDS

The active voices and the mix time.

- Direction: S->C
- Version: 3

| Offset       | Size            | Type       | Name        | Meaning                                                          |
| ------------ | --------------- | ---------- | ----------- | ---------------------------------------------------------------- |
| 0            | 4               | u32 LE     | CacheSize   | Capacity of the wave data cache in bytes.                        |
| 4            | 4               | u32 LE     | LockedBytes | Sum of the sizes of the cache entries with a lock count above 0. |
| 8            | 2               | u16 LE     | VoiceCount  | Number of voice entries.                                         |
| 10           | 2               | u16 LE     | VoiceSlots  | Number of voice slots.                                           |
| 12           | 12              | f32 LE x 3 | Listener    | Position of the first listener.                                  |
| 24           | 4               | f32 LE     | AverageMix  | Average of the last 16 mix times, in seconds.                    |
| 28           | 4               | f32 LE     | MaxMix      | Maximum of the last 16 mix times, in seconds.                    |
| 32           | 63 x VoiceCount | entry      | Voices      | Voices.                                                          |
| after Voices | var             | bytes      | Symbols     | Symbol data. Not defined.                                        |

Voice (63 bytes):

| Offset | Size | Type       | Name        | Meaning                                                                                  |
| ------ | ---- | ---------- | ----------- | ---------------------------------------------------------------------------------------- |
| 0      | 12   | f32 LE x 3 | Position    | Origin of the voice.                                                                     |
| 12     | 4    | f32 LE     | Elapsed     | Play position of the voice in seconds. 0 when the voice starts.                          |
| 16     | 4    | f32 LE     | Duration    | Duration of the sound file in seconds. 0 when the sound file is not loaded.              |
| 20     | 4    | f32 LE     | Volume      | Start volume of the voice multiplied by its fade gain. The fade gain is 0 to 1.          |
| 24     | 4    | f32 LE     | LeftVolume  | Left volume, 0 to 1.                                                                     |
| 28     | 4    | f32 LE     | RightVolume | Right volume, 0 to 1.                                                                    |
| 32     | 4    | f32 LE     | Pitch       | Current pitch. 1.0 is normal pitch. The sound event updates it while the voice plays.    |
| 36     | 4    | i32 LE     | Guid        | Voice GUID. A negative value is a client voice.                                          |
| 40     | 4    | u32 LE     | EventGuid   | GUID of the sound event that started the voice.                                          |
| 44     | 4    | u32 LE     | ParentGuid  | GUID of the mix graph voice that this voice feeds, or 0 when the voice has no parent.    |
| 48     | 4    | i32 LE     | Source      | Source entity index.                                                                     |
| 52     | 4    | f32 LE     | Level       | Level in dB. The default is -90.                                                         |
| 56     | 2    | u16 LE     | Index       | Voice slot index.                                                                        |
| 58     | 2    | u16 LE     | NameSymbol  | Symbol of the sound file name.                                                           |
| 60     | 2    | u16 LE     | EventSymbol | Symbol of the sound event name.                                                          |
| 62     | 1    | u8         | Flags       | Bit 0 = the sound file loops. Bit 1 = paused. Bit 2 = the voice drives a lip-sync mouth. |

## SEVT

The sound events. Each packet has one kind 0 entry for each active sound event, and one entry for each state change since the last packet.

- Direction: S->C
- Version: 3

| Offset       | Size       | Type       | Name     | Meaning                                   |
| ------------ | ---------- | ---------- | -------- | ----------------------------------------- |
| 0            | 4          | u32 LE     | Count    | Number of events.                         |
| 4            | 4          | f32 LE     | PeakTime | Peak stack time in ms, held for 1 second. |
| 8            | 12         | f32 LE x 3 | Listener | Listener position, as in SNDS.            |
| 20           | 32 x Count | entry      | Events   | Events.                                   |
| after Events | var        | bytes      | Symbols  | Symbol data. Not defined.                 |

Event (32 bytes):

| Offset | Size | Type       | Name        | Meaning                                                                                                         |
| ------ | ---- | ---------- | ----------- | --------------------------------------------------------------------------------------------------------------- |
| 0      | 8    | f64 LE     | Time        | Kind 0: the time when the event started. Other kinds: the current sound system time.                            |
| 8      | 4    | u32 LE     | Guid        | GUID of the sound event.                                                                                        |
| 12     | 4    | i32 LE     | SourceIndex | Source entity index. -1 for an event that VSPS started.                                                         |
| 16     | 12   | f32 LE x 3 | Position    | Kind 0: origin of the first voice of the event. Other kinds: 0.                                                 |
| 28     | 2    | u16 LE     | NameSymbol  | Symbol of the event name. The default is `SoundEvent_error`.                                                    |
| 30     | 2    | u16 LE     | Kind        | 0 = active. 1 = update from a server message. 2 = stop. 3 = blocked. 4 = start. 7 = stopped by a stop operator. |
