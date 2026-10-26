# Protocol versions

## Overview

The default version changed with AINF and with the new tags ADON and ANOF. Other payloads changed with no version change, so the version does not identify all layouts. The tag and the payload size identify them.

Each step adds one change:

| Step | Version | Change                                                                                                                                        |
| ---- | ------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| 1    | 207     | AINF has no command line. Pipe transport, one client. CVAR for each cvar, CFGV (129 bytes) for each configuration value. No ADON and no ANOF. |
| 2    | 209     | The server sends ADON.                                                                                                                        |
| 3    | 210     | The server sends ANOF.                                                                                                                        |
| 4    | 210     | CFGV shrinks to 69 bytes. Same version.                                                                                                       |
| 5    | 211     | AINF adds the command-line arguments.                                                                                                         |
| 6    | 212     | AINF moves the flags byte after the argument fields. Same transport.                                                                          |
| 7    | 212     | Socket transport with client slots (`-vconmax`). USRN tag. No pipes.                                                                          |
| 8    | 212     | CVRB version 1, layout A, for the full cvar list.                                                                                             |
| 9    | 212     | CVAR grows to 201 bytes. CVRB layout B. No CFGV.                                                                                              |
| 10   | 212     | CVRB layout C with a string pool. No CVAR.                                                                                                    |
| 11   | 212     | CVRB version 2 (KV3). The protocol that the other pages describe.                                                                             |

A game keeps the protocol of the engine branch that it ships from. Thus a current server can use any of these steps.

These parts are the same in all builds:

- The 12-byte header layout.
- CHAN (S->C) with 58-byte entries, PRNT with the text at offset 28, MODM, SVME and SVMI.
- PING and PONG with version 1. AUTO with version 2.
- The CHAN (C->S) processing on the network thread.
- The log lines "Message Version Mismatch" and "VConsole Rejecting Message".
- Port 29000, the history size 8192 KB, and the startup wait with `-wv` or `-vconsole`.
- The BUGS reply with version 3.

The CMND handler accepts only the default version of its build. It drops a CMND with any other version and logs "Message Version Mismatch". It sends no error packet. The default version is always the AINF version.

## Versions 207 to 210

Versions 207, 209 and 210 have the same payload layouts. They differ only in the ADON and ANOF packets ([Overview](#overview)). The tables below show 210. Builds with version 207 or 209 use their own version in its place.

The Handle field (u16 BE at header offset 10) holds a pipe ID. Pipe IDs start at 1. The server gives a packet with a Handle other than 0 to the pipe layer, with no tag or version check.

| Tag  | Meaning                    | Payload                                                    |
| ---- | -------------------------- | ---------------------------------------------------------- |
| PPCR | Pipe create request.       | 37 bytes: u16 LE pipe ID, u16 LE pipe type, char[33] name. |
| PPOR | Pipe open request.         | cstr.                                                      |
| PPOA | Pipe open acknowledgement. | None.                                                      |
| PPCL | Pipe close.                | cstr reason.                                               |

After connect, at no fixed time, the server sends PPCR for each pipe that the game registered. A server with no pipes sends no PPCR. On disconnect, the server sends PPCL "VConsole disconnect" for each open pipe. A console client MUST NOT send PPOR. It MUST skip PPCR and PPCL.

| Tag                                            | Direction | Version |
| ---------------------------------------------- | --------- | ------- |
| AINF, ADON, PRNT, MODM, SVME, SVMI, CVAR, CFGV | S->C      | 210     |
| CHAN, CMND, ACTV, ANOF, PPCR, PPOR, PPOA, PPCL | both      | 210     |
| AUTO                                           | both      | 2       |
| PING                                           | C->S      | 1       |
| PONG                                           | S->C      | 1       |
| VFCS                                           | C->S      | 210     |

Version 210 has no USRN and no CVRB.

AINF (210), 77 bytes, no arguments:

| Offset | Size | Type     | Name    | Meaning                                                                                                                       |
| ------ | ---- | -------- | ------- | ----------------------------------------------------------------------------------------------------------------------------- |
| 0      | 4    | u32 BE   | NameCRC | CRC-32 of the full path of the executable. The path is the text that the platform returns for the module file name.           |
| 4      | 8    | u64 BE   | HWND    | Window handle.                                                                                                                |
| 12     | 32   | char[32] | ExeName | Executable base name.                                                                                                         |
| 44     | 32   | char[32] | GameDir | Name of the game directory (the mod).                                                                                         |
| 76     | 1    | u8       | Flags   | 0x01 = always set, 0x02 = tools mode, 0x04 = customer machine, 0x08 = set in most builds. The other bits are not initialized. |

CVAR (210), 81 bytes. The server sends one CVAR packet for each cvar and command on connect, and one for each new cvar.

| Offset | Size | Type     | Name  | Meaning                                           |
| ------ | ---- | -------- | ----- | ------------------------------------------------- |
| 0      | 64   | char[64] | Name  | Name.                                             |
| 64     | 8    | u64 BE   | Flags | FCVAR flags.                                      |
| 72     | 4    | f32 BE   | Min   | Minimum value. 0 for a command.                   |
| 76     | 4    | f32 BE   | Max   | Maximum value. 0 for a command.                   |
| 80     | 1    | u8       | Bits  | 0x01 = command. 0x02 = command with autocomplete. |

CFGV (210), early form, 129 bytes:

| Offset | Size | Type     | Name  | Meaning                                                                                                                |
| ------ | ---- | -------- | ----- | ---------------------------------------------------------------------------------------------------------------------- |
| 0      | 64   | char[64] | Name  | Name.                                                                                                                  |
| 64     | 64   | char[64] | Scope | Name of the scope of the configuration value: "application", "machine" or "per-user". Empty when the scope is invalid. |
| 128    | 1    | u8       | Bits  | 0x01 = the configuration value has a completion callback.                                                              |

CFGV (210), late form, 69 bytes:

| Offset | Size | Type     | Name  | Meaning                                                                                                                                                                                                                                                             |
| ------ | ---- | -------- | ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 64   | char[64] | Name  | Name.                                                                                                                                                                                                                                                               |
| 64     | 4    | u32 LE   | Scope | Scope of the configuration value as a number. 0 = application, 1 = launch, 2 = machine, 3 = per-user. 0xFFFFFFFF = invalid. Some builds have no launch scope: 0 = application, 1 = machine, 2 = per-user. A client cannot tell these builds apart from the payload. |
| 68     | 1    | u8       | Bits  | 0x01 = the configuration value has a completion callback.                                                                                                                                                                                                           |

A CFGV packet describes one value of the configuration system. It does not describe a cvar.

When the application information changes, the server sends AINF again, only while a client is connected from 127.0.0.1.

Connect sequence (210 and 211):

1. AINF.
2. ADON, from version 209.
3. CHAN.
4. MODM, one packet for each module, only when tier0 tracks modules ([MODM](console.md#modm)).
5. SVME and SVMI, with the same condition.
6. PRNT "VConsole Connected.".
7. The PRNT history, between banners. The banners come only when the history is not empty.
8. CVAR, one packet for each cvar and command.
9. CFGV, one packet for each configuration value.

Transport (210 and 211):

- The server accepts one client only.
- `-vconmaxlisten <n>` (default 1) sets the listen backlog, not a number of slots.
- The receive buffer is 8 MiB in some builds. Other builds start at 1 MiB and grow to 8 MiB.
- There is no `-vconmax`, no `vcon_clients` and no USRN.
- On Windows, `-notransport2` selects an older transport with the same port and packets. Its listen backlog is always 1, and it reads a packet only when the packet fits a 256 KiB receive buffer. A client MUST NOT send a packet larger than 256 KiB to such a server.

## Version 211

The only change from 210 is the AINF payload. All tags keep their 210 meaning with version 211.

AINF (211), 85 bytes and the arguments:

| Offset | Size | Type      | Name     | Meaning                                                                           |
| ------ | ---- | --------- | -------- | --------------------------------------------------------------------------------- |
| 0      | 4    | u32 BE    | NameCRC  | CRC-32 of the full path of the executable, as in version 210.                     |
| 4      | 8    | u64 BE    | HWND     | Window handle.                                                                    |
| 12     | 32   | char[32]  | ExeName  | Executable base name.                                                             |
| 44     | 32   | char[32]  | GameDir  | Name of the game directory (the mod).                                             |
| 76     | 1    | u8        | Flags    | 0x01 = always set, 0x02 = tools mode, 0x04 = customer machine, 0x08 = always set. |
| 77     | 4    | u32 BE    | ArgCount | Number of arguments. Not aligned.                                                 |
| 81     | 4    | u32 BE    | ArgSize  | Total size of Args. Not aligned.                                                  |
| 85     | var  | cstr list | Args     | Arguments.                                                                        |

## Version 212, first form

The only change from 211 is the AINF payload. It has the current layout ([AINF](console.md#ainf)), without flag 0x10. The transport, the pipes, the tags, CVAR (81 bytes), CFGV (69 bytes) and the connect sequence are the same as in 211.

## Version 212, second form

Changes from the first form:

- Socket transport with client slots. `-vconmax <n>` sets the number. The console commands `vcon_clients` and `vcon_clear_buffered_log` exist.
- The client can send USRN.
- The pipe tags are gone. The Handle field is always 0.
- AINF flag 0x10 (dedicated server) exists.
- `-vconmaxlisten` is gone.

Connect sequence (212, second form, before CVRB):

1. AINF.
2. CHAN.
3. PRNT "Connection in progress...".
4. ADON.
5. MODM, SVME and SVMI, only when tier0 tracks modules ([MODM](console.md#modm)).
6. CVAR, one packet for each cvar and command.
7. CFGV, one packet for each configuration value.
8. The PRNT history.
9. PRNT "VConsole client connection from %d.%d.%d.%d".

CVRB replaces steps 6 and 7 in later builds.

## CVRB version 1, layout A

The server sends one CVRB packet with all cvars and commands on connect. It sends CVRB only when there are more than 100 cvars. With 100 cvars or fewer, or when it cannot queue the packet, it sends one CVAR packet for each cvar.

The CVRB version is 1. The payload header is little-endian. The entry fields are big-endian.

| Offset | Size | Type   | Name       | Meaning                                                                           |
| ------ | ---- | ------ | ---------- | --------------------------------------------------------------------------------- |
| 0      | 4    | u32 LE | Count      | Number of entries.                                                                |
| 4      | 4    | u32 LE | PoolSize   | Size of the name pool, a multiple of 4.                                           |
| 8      | 4    | u32 LE | RawSize    | Size of the data before compression.                                              |
| 12     | 4    | u32 LE | PackedSize | Size of the compressed data, the 8-byte LZSS header included. 0 = no compression. |
| 16     | var  | bytes  | Data       | The name pool, then the entries.                                                  |

The name pool holds the names in entry sequence, each a cstr of 64 bytes or fewer, NUL included. The server leaves out cvars with empty names. Zero bytes pad the pool to a multiple of 4.

The server compresses the data only when the result is smaller. Otherwise PackedSize is 0 and the data is raw. Compressed data starts with the 4 bytes `LZSS`, then a u32 LE with the size before compression, then an LZSS stream:

- The stream is groups of one flag byte and up to 8 items. The flag bits are read from the least significant bit.
- A 0 bit is one literal byte.
- A 1 bit is a 2-byte reference b0, b1. Offset = (b0 << 4) | (b1 >> 4). Length = (b1 & 0x0F) + 1. Copy Length bytes, one at a time, from Offset + 1 bytes before the current output position.
- A reference with (b1 & 0x0F) = 0 ends the stream.
- The window is 4096 bytes. The output size MUST equal the size in the header.

Entry, layout A (17 bytes):

| Offset | Size | Type   | Name  | Meaning                              |
| ------ | ---- | ------ | ----- | ------------------------------------ |
| 0      | 8    | u64 BE | Flags | FCVAR flags.                         |
| 8      | 4    | f32 BE | Min   | Minimum value.                       |
| 12     | 4    | f32 BE | Max   | Maximum value.                       |
| 16     | 1    | u8     | Bits  | 0x01 = command. 0x02 = autocomplete. |

## CVAR (201 bytes) and CVRB layout B

These builds have no CFGV.

CVAR, 201 bytes:

| Offset | Size | Type     | Name  | Meaning                                                                       |
| ------ | ---- | -------- | ----- | ----------------------------------------------------------------------------- |
| 0      | 64   | char[64] | Name  | Name.                                                                         |
| 64     | 8    | u64 BE   | Flags | FCVAR flags.                                                                  |
| 72     | 64   | char[64] | Min   | Minimum value as text. Empty when the cvar has no minimum, and for a command. |
| 136    | 64   | char[64] | Max   | Maximum value as text. Empty when the cvar has no maximum, and for a command. |
| 200    | 1    | u8       | Bits  | 0x01 = command. 0x02 = autocomplete.                                          |

Min and Max have the same meaning as in the 81-byte CVAR and in layout A. They are text instead of f32.

CVRB layout B has the same 16-byte header and version 1 as layout A. The 100-cvar rule is the same. Entry, layout B (137 bytes):

| Offset | Size | Type     | Name  | Meaning                                                                       |
| ------ | ---- | -------- | ----- | ----------------------------------------------------------------------------- |
| 0      | 8    | u64 BE   | Flags | FCVAR flags.                                                                  |
| 8      | 64   | char[64] | Min   | Minimum value as text. Empty when the cvar has no minimum, and for a command. |
| 72     | 64   | char[64] | Max   | Maximum value as text. Empty when the cvar has no maximum, and for a command. |
| 136    | 1    | u8       | Bits  | 0x01 = command. 0x02 = autocomplete.                                          |

To tell layout A from layout B, compare RawSize with PoolSize + 17 x Count and with PoolSize + 137 x Count.

## CVRB layout C

These builds have no CVAR. The server always sends CVRB. The version is still 1.

| Offset | Size | Type   | Name       | Meaning                                                         |
| ------ | ---- | ------ | ---------- | --------------------------------------------------------------- |
| 0      | 4    | u32 LE | Count      | Number of entries.                                              |
| 4      | 4    | u32 LE | RawSize    | Size of the data before compression.                            |
| 8      | 4    | u32 LE | PackedSize | Size of the compressed data. 0 = no compression.                |
| 12     | var  | bytes  | Data       | String pool, then the entries. LZSS compression as in layout A. |

The data starts with the string pool, so bytes 12 to 15 are `CSP1` when PackedSize is 0, and `LZSS` when the data is compressed. To tell layout C from layouts A and B, read the 4 bytes at offset 12. `CSP1` or `LZSS` means a 12-byte header (layout C). Any other value means a 16-byte header (layout A or B).

The string pool has these little-endian fields:

1. i32 magic `0x31505343` (the bytes `CSP1`).
2. u16 free list head.
3. i32 number of hash buckets, then one u16 for each bucket.
4. i32 number of elements, element 0 included.
5. For each element from 1: u16 next, u8 reference count, then a cstr.

A string handle is an element index. The pool compares strings with no regard to case. Two strings that differ only in case share one handle. The first string wins. The pool format is the same in all builds with layout C.

Entry, layout C (19 bytes):

| Offset | Size | Type   | Name    | Meaning                                                                                 |
| ------ | ---- | ------ | ------- | --------------------------------------------------------------------------------------- |
| 0      | 8    | u64 BE | Flags   | FCVAR flags.                                                                            |
| 8      | 2    | u16 LE | Name    | Handle of the name.                                                                     |
| 10     | 2    | u16 LE | Value   | Handle of the current value as text.                                                    |
| 12     | 2    | u16 LE | Default | Handle of the default value as text.                                                    |
| 14     | 2    | u16 LE | Min     | Handle of the minimum value as text. Empty text when the cvar has no minimum.           |
| 16     | 2    | u16 LE | Max     | Handle of the maximum value as text. Empty text when the cvar has no maximum.           |
| 18     | 1    | u8     | Bits    | 0x01 = command. 0x02 = autocomplete. 0x04 = the current value equals the default value. |

For a command, the server does not write the four string handles. A client MUST ignore them.

The server also sends a CVRB with one entry in three cases. A game module creates a cvar. A game module creates a command. A cvar value changes. The server sends it to each connected client. The entry holds the state of the cvar at that time. A client merges it as in [CVRB](console.md#cvrb).

In early builds with layout C, bit 0x02 marks only commands with autocomplete. Later builds also set bit 0x02 on a cvar that has a completion callback.

## CVRB version 2

CVRB version 2 is the KV3 form in [CVRB](console.md#cvrb). It replaces layout C.

## Load conditions

The rule has four forms. Each form replaces the one before it.

| Form | Rule                                                                                                    |
| ---- | ------------------------------------------------------------------------------------------------------- |
| 1    | `-vconsole` or `-vconport`, or (`isgame` and no `-novconsole` and no `-nv`).                            |
| 2    | `-vconsole` or `-vconport`, or (`isgame` and `-tools` and no `-nv`).                                    |
| 3    | `-tools` and (`isgame`, `-vconsole` or `-vconport`) and no `-nv` and no `-novconsole`.                  |
| 4    | (`-tools` or `-insecure`) and (`isgame`, `-vconsole` or `-vconport`) and no `-nv` and no `-novconsole`. |

Before form 4, `-insecure` has no effect on VConsole2. [Command-line parameters](transport.md#command-line-parameters) describes form 4.

## Client compatibility

A client that supports all versions MUST do these steps:

1. Read the version from the AINF header. Use it for CMND, CHAN, ACTV and VFCS. Use version 1 for PING and version 2 for AUTO. Use [Tags](readme.md#tags) for all other tags.
2. Read AINF by its version: [Versions 207 to 210](#versions-207-to-210), [Version 211](#version-211), [AINF](console.md#ainf).
3. Accept all forms of the cvar list. Select the form by the tag and the payload, not by the version:
    - CVAR with 81 bytes or 201 bytes.
    - CFGV with 129 bytes or 69 bytes.
    - CVRB version 1: see [CVRB layout C](#cvrb-layout-c) for the header test.
    - CVRB version 2 with KV3.
4. Before CVRB version 2, the cvar list has no help text. Before layout C, it has no current value and no default value. To get a value, send CMND with the cvar name and read the PRNT reply.
5. On a server with the pipe transport (versions 207 to 211 and the first form of 212), expect one client slot only. Do not send USRN to these servers. They reject it as a tag with no handler.
6. Detect the end of the history replay by the server's marker. Servers with the pipe transport send PRNT "VConsole Connected." before the history and the first CVAR after it. Servers with the socket transport send "VConsole client connection from" after it.
7. To start the server, pass `-vconport 29000` or `-vconsole` ([Wait for a client](transport.md#wait-for-a-client)). Load condition forms 3 and 4 also need `-tools`, or `-insecure` in form 4 ([Load conditions](#load-conditions)).
