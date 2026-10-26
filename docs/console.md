# Console packets

The modules `vconcomm`, `engine2` and `client` send and receive the packets on this page.

## AINF

Application information. The server sends it first on each connect, and again to all clients when the application information changes. The server sends two AINF packets at each process start. The second one has a changed HWND.

- Direction: S->C
- Version: 212
- Module: vconcomm

| Offset | Size | Type      | Name     | Meaning                                                                                                                                                      |
| ------ | ---- | --------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 0      | 4    | u32 BE    | NameCRC  | CRC-32 of ExeName, with no NUL. For example, CRC-32 of `hello` is `0x3610A686`. [Protocol versions](versions.md) gives the CRC input of versions 207 to 211. |
| 4      | 8    | u64 BE    | HWND     | Window handle of the game.                                                                                                                                   |
| 12     | 32   | char[32]  | ExeName  | Executable base name, with no extension.                                                                                                                     |
| 44     | 32   | char[32]  | GameDir  | Name of the game directory (the mod).                                                                                                                        |
| 76     | 4    | u32 BE    | ArgCount | Number of command-line arguments.                                                                                                                            |
| 80     | 4    | u32 BE    | ArgSize  | Total size of Args in bytes.                                                                                                                                 |
| 84     | 1    | u8        | Flags    | See the next table.                                                                                                                                          |
| 85     | var  | cstr list | Args     | ArgCount strings. The first string is the full path of the executable.                                                                                       |

| Bit  | Meaning                                                                         |
| ---- | ------------------------------------------------------------------------------- |
| 0x01 | Always set. 64-bit process.                                                     |
| 0x02 | The game runs in tools mode (`-tools`). Set only in the second AINF of a start. |
| 0x04 | The game runs on a customer (retail) machine.                                   |
| 0x08 | Always set.                                                                     |
| 0x10 | The application is a dedicated server ([VFCS](#vfcs)).                          |

A client MAY ignore an AINF whose NameCRC is 0.

## CHAN (S->C)

The list of log channels. The server sends it on connect with all channels. It sends a single-entry CHAN to all clients when a channel registers, and when the verbosity or the flags of a channel change. A client MUST merge a single-entry CHAN into its list by ChannelID.

- Direction: S->C
- Version: 212
- Module: vconcomm

| Offset | Size       | Type   | Name    | Meaning            |
| ------ | ---------- | ------ | ------- | ------------------ |
| 0      | 2          | u16 BE | Count   | Number of entries. |
| 2      | 58 x Count | entry  | Entries | Channel entries.   |

Entry (58 bytes):

| Offset | Size | Type     | Name             | Meaning                                                                                                   |
| ------ | ---- | -------- | ---------------- | --------------------------------------------------------------------------------------------------------- |
| 0      | 4    | u32 BE   | ChannelID        | Standard CRC-32 (the zlib polynomial) of Name with its NUL byte. CRC-32 of `"Console\0"` is `0x094098AD`. |
| 4      | 4    | u32 BE   | DefaultFlags     | Default channel flags. Opaque.                                                                            |
| 8      | 4    | u32 BE   | Flags            | Current channel flags. Opaque.                                                                            |
| 12     | 4    | u32 BE   | DefaultVerbosity | Default verbosity.                                                                                        |
| 16     | 4    | u32 BE   | Verbosity        | Current verbosity.                                                                                        |
| 20     | 1    | u8       | R                | Red.                                                                                                      |
| 21     | 1    | u8       | G                | Green.                                                                                                    |
| 22     | 1    | u8       | B                | Blue.                                                                                                     |
| 23     | 1    | u8       | A                | Alpha.                                                                                                    |
| 24     | 32   | char[32] | Name             | Channel name, 31 characters or fewer.                                                                     |
| 56     | 1    | u8       | Zero             | Always 0.                                                                                                 |
| 57     | 1    | u8       | One              | Always 1.                                                                                                 |

A client SHOULD use the channel color when R, G and B are not all 0.

| Verbosity | Name      |
| --------- | --------- |
| 0         | Off       |
| 1         | Essential |
| 2         | Default   |
| 3         | Detailed  |
| 4         | Max       |

## CHAN (C->S)

Sets the verbosity and the flags of one channel. The server processes it on the network thread immediately. It matches Name case-insensitively and ignores an unknown name. Each change makes the server send a single-entry CHAN (S->C) to every client. A client SHOULD send in Flags only a value that it read from CHAN (S->C).

- Direction: C->S
- Version: 212
- Module: vconcomm

| Offset | Size | Type     | Name      | Meaning                                                      |
| ------ | ---- | -------- | --------- | ------------------------------------------------------------ |
| 0      | 33   | char[33] | Name      | Channel name. MUST end with a NUL within the first 32 bytes. |
| 33     | 4    | u32 BE   | Verbosity | New verbosity.                                               |
| 37     | 4    | u32 BE   | Flags     | New channel flags.                                           |

## PRNT

One log line. The server sends one PRNT packet to all clients for each log line whose severity the channel verbosity shows.

- Direction: S->C
- Version: 212
- Module: vconcomm

| Offset     | Size         | Type     | Name          | Meaning                                                   |
| ---------- | ------------ | -------- | ------------- | --------------------------------------------------------- |
| 0          | 4            | u32 BE   | ChannelID     | Channel ID ([CHAN (S->C)](#chan-s-c)).                    |
| 4          | 4            | u32 BE   | ContextFlags  | Current Flags of the channel.                             |
| 8          | 4            | u32 BE   | Severity      | See the next table.                                       |
| 12         | 1            | u8       | R             | Red.                                                      |
| 13         | 1            | u8       | G             | Green.                                                    |
| 14         | 1            | u8       | B             | Blue.                                                     |
| 15         | 1            | u8       | A             | Alpha. All four bytes 0 means "use the channel color".    |
| 16         | 4            | u32 BE   | Time          | Milliseconds since the process started.                   |
| 20         | 4            | u32 BE   | MetadataSize  | Size of the metadata blocks in bytes.                     |
| 24         | 4            | bytes[4] | Uninitialized | Any value. A client MUST ignore it.                       |
| 28         | var          | cstr     | Text          | The raw bytes of the log line. It usually ends with `\n`. |
| after Text | MetadataSize | blocks   | Metadata      | Metadata blocks.                                          |

Length = 12 + 28 + (text bytes + 1) + MetadataSize.

| Severity | Name                | Meaning                                       |
| -------- | ------------------- | --------------------------------------------- |
| 0        | LS_MORE_DETAILED    | Debug message with more detail than Detailed. |
| 1        | LS_DETAILED         | Debug message.                                |
| 2        | LS_MESSAGE          | Message.                                      |
| 3        | LS_WARNING          | Warning.                                      |
| 4        | LS_ASSERT           | Message from an assert.                       |
| 5        | LS_ERROR            | Error.                                        |
| 6        | LS_HIGHEST_SEVERITY | Limit value. It is not a real severity.       |

Severity 0 needs channel verbosity Max. Severity 1 needs Detailed. Severity 2 needs Default. Severities 3, 4 and 5 need Essential.

Metadata block:

| Offset | Size   | Type   | Name   | Meaning                |
| ------ | ------ | ------ | ------ | ---------------------- |
| 0      | 8      | u64 BE | TypeID | Block type.            |
| 8      | 4      | u32 BE | Length | Size of Data in bytes. |
| 12     | Length | bytes  | Data   | Block data.            |

The server leaves out metadata blocks that the channel verbosity hides. It removes the `tothread` block (TypeID `0x6461657268746F74`). A line with a `tothread` block goes only to one client and stays out of the history.

A client SHOULD accept invalid UTF-8 in Text.

## ADON

The addon list. The server sends it on connect, and to all clients when the addon list changes.

- Direction: S->C
- Version: 212
- Module: vconcomm

| Offset | Size      | Type      | Name      | Meaning                       |
| ------ | --------- | --------- | --------- | ----------------------------- |
| 0      | 2         | u16 BE    | Count     | Number of addons.             |
| 2      | 2         | u16 BE    | NamesSize | Total size of Names in bytes. |
| 4      | NamesSize | cstr list | Names     | Addon names.                  |

With no addons, the payload is 4 zero bytes.

## Call stacks

### MODM

One loaded module. Clients use it to show module names in call stacks.

The server sends MODM, SVME and SVMI only when tier0 tracks loaded modules. It sends them on connect, after ADON, and later to all clients when a module loads. It sends SVME and SVMI only when a script VM registers a call-stack encoder.

- Direction: S->C
- Version: 212
- Module: vconcomm

| Offset        | Size     | Type   | Name        | Meaning                                                           |
| ------------- | -------- | ------ | ----------- | ----------------------------------------------------------------- |
| 0             | 8        | u64 BE | BaseAddress | Module base address.                                              |
| 8             | 4        | u32 BE | Size        | Module size in bytes.                                             |
| 12            | 4        | u32 BE | Index       | Module index.                                                     |
| 16            | 1        | u8     | Type        | Module type.                                                      |
| 17            | 2        | u16 BE | InfoSize    | Size of Name, DebugID and Path in bytes.                          |
| 19            | var      | cstr   | Name        | Module name.                                                      |
| after Name    | 24 or 12 | bytes  | DebugID     | 24 bytes for Type 1. 12 bytes for Type 2. Absent for other types. |
| after DebugID | var      | cstr   | Path        | Module path. Only for Type 1 and Type 2.                          |

### SVME

The description of one script VM call-stack encoder. [MODM](#modm) gives the send condition.

- Direction: S->C
- Version: 212
- Module: vconcomm

| Offset | Size | Type   | Name       | Meaning                         |
| ------ | ---- | ------ | ---------- | ------------------------------- |
| 0      | 8    | u64 BE | VMID       | VM ID.                          |
| 8      | 8    | u64 BE | IndexMask  | Bit mask of the function index. |
| 16     | 1    | u8     | IndexShift | Shift of the function index.    |
| 17     | 8    | u64 BE | LineMask   | Bit mask of the line offset.    |
| 25     | 1    | u8     | LineShift  | Shift of the line offset.       |
| 26     | 8    | u64 BE | SignMask   | Sign bit mask, after the shift. |
| 34     | 1    | u8     | One        | Always 1.                       |

### SVMI

Information for one script VM call-stack index. The server sends one SVMI for each encoding index after SVME. [MODM](#modm) gives the send condition.

- Direction: S->C
- Version: 212
- Module: vconcomm

| Offset        | Size | Type   | Name    | Meaning        |
| ------------- | ---- | ------ | ------- | -------------- |
| 0             | 8    | u64 BE | VMID    | VM ID.         |
| 8             | 4    | u32 BE | Index   | Index.         |
| 12            | 4    | u32 BE | Value   | Value.         |
| 16            | var  | cstr   | String1 | First string.  |
| after String1 | var  | cstr   | String2 | Second string. |

## CVRB

The cvar and command list. The server sends it at these times:

- On connect, with all cvars and commands, to all clients.
- When a cvar value changes, with one entry. The server does not send this packet for a cvar with flag bit `0x1000000000`. Such a cvar is still in the full list.
- When the game registers a new cvar or command, with one entry.

The server never signals a removal. A client MUST merge every CVRB, full or single-entry, into its list by m_szName. A reconnecting client SHOULD clear its list before it merges the first full CVRB.

- Direction: S->C
- Version: 2
- Module: vconcomm

The payload is one KV3 object ([KV3 payloads](readme.md#kv3-payloads)). The root has one key:

| Key          | Type             | Meaning                              |
| ------------ | ---------------- | ------------------------------------ |
| m_vecDetails | array of objects | One object for each cvar or command. |

Each object has these 14 keys.

| Key                | Type             | Meaning                                                                                                         |
| ------------------ | ---------------- | --------------------------------------------------------------------------------------------------------------- |
| m_szName           | string           | Name.                                                                                                           |
| m_nFlags           | uint64           | FCVAR flags. Values use more than 32 bits.                                                                      |
| m_nType            | int              | Value type. -1 for a command. See the next table.                                                               |
| m_szValue          | string           | Current value as text. Empty for a command.                                                                     |
| m_szGroupName      | string           | Always empty.                                                                                                   |
| m_szHelpString     | string           | Help text. Can be empty.                                                                                        |
| m_szDefault        | string           | Default value as text. Empty for a command.                                                                     |
| m_szMin            | string           | Minimum value as text, or empty.                                                                                |
| m_szMax            | string           | Maximum value as text, or empty.                                                                                |
| m_vecEnum          | array of objects | Enum values. Each object has m_szName (string), m_szFriendlyName (string) and m_nFlags (int64, the enum value). |
| m_bCommand         | bool             | True for a command.                                                                                             |
| m_bCanAutoComplete | bool             | True if the entry has autocomplete.                                                                             |
| m_bIsDefault       | bool             | True if the value is the default value.                                                                         |
| m_bIsFlagsEnum     | bool             | True if the enum values are bit flags.                                                                          |

For a command, the string keys are empty, except m_szName and m_szHelpString.

| m_nType | Name           | Example value |
| ------- | -------------- | ------------- |
| -1      | none (command) |               |
| 0       | bool           | `false`       |
| 1       | int16          |               |
| 2       | uint16         |               |
| 3       | int32          | `0`           |
| 4       | uint32         | `0`           |
| 5       | int64          |               |
| 6       | uint64         | `0`           |
| 7       | float32        | `0.02`        |
| 8       | float64        |               |
| 9       | string         | (text)        |
| 10      | color          | `128 128 128` |
| 11      | vector2        |               |
| 12      | vector3        | `1 0.5 0.25`  |
| 13      | vector4        |               |
| 14      | qangle         |               |
| 15      | vectorws       |               |

A client SHOULD accept other m_nType values.

The server writes m_vecEnum values only for a cvar that has flag bit `0x2000000000`. Their m_szFriendlyName values are empty. The cvar list skips hidden cvars ([Hidden commands](#hidden-commands)).

A client SHOULD accept unknown keys.

## CMND (C->S)

Runs a console command. [Command execution and output](#command-execution-and-output) gives the processing rules.

- Direction: C->S
- Version: 212
- Module: engine2

| Offset | Size | Type | Name    | Meaning               |
| ------ | ---- | ---- | ------- | --------------------- |
| 0      | var  | cstr | Command | Console command text. |

Output arrives as PRNT packets ([Output correlation](#output-correlation)).

## CMND (S->C)

A command for the client itself.

- Direction: S->C
- Version: 212
- Module: vconcomm, client

| Offset | Size | Type | Name    | Meaning                 |
| ------ | ---- | ---- | ------- | ----------------------- |
| 0      | var  | cstr | Command | Command for the client. |

| Command             | Sender                                   | Meaning                                                                            |
| ------------------- | ---------------------------------------- | ---------------------------------------------------------------------------------- |
| `clear`             | vconcomm, console command `clear`        | Clear the console view.                                                            |
| `clearall`          | vconcomm, console command `clearall`     | Clear all console views.                                                           |
| `vc_exit <context>` | vconcomm                                 | Close the client.                                                                  |
| `animdebugger`      | client, console command `ent_debug_anim` | Open the animation debugger. Sent when `ent_debug_anim` sets a valid debug entity. |

A client MUST NOT send the text of a received CMND back to the server.

## AUTO (C->S)

An autocomplete request.

- Direction: C->S
- Version: 2
- Module: engine2

| Offset | Size | Type      | Name  | Meaning                   |
| ------ | ---- | --------- | ----- | ------------------------- |
| 0      | 128  | char[128] | Text  | Partial command text.     |
| 128    | 4    | u32 BE    | Count | 0. The server ignores it. |
| 132    | 1    | u8        | Flags | 0. The server ignores it. |

The payload is 133 bytes.

## AUTO (S->C)

The autocomplete reply. The server sends it to all clients.

- Direction: S->C
- Version: 2
- Module: engine2

| Offset | Size | Type      | Name        | Meaning                                                                                             |
| ------ | ---- | --------- | ----------- | --------------------------------------------------------------------------------------------------- |
| 0      | 128  | char[128] | Text        | Copy of the request text.                                                                           |
| 128    | 4    | u32 BE    | Count       | Number of completions.                                                                              |
| 132    | 1    | u8        | Flags       | Bit 0 is 1 when the command has FCVAR bit `0x800000` in its CVRB m_nFlags. Other bits: not defined. |
| 133    | var  | cstr list | Completions | Count completion strings.                                                                           |

The server sends a reply only for a command that has completions for its argument. The server sends no reply when no completion matches. The server removes trailing spaces from Text and keeps the case. Each completion is a full command line, for example `map <name>`. A client MUST match a reply to its request by a case-insensitive compare of Text, after it removes trailing spaces from both. A client MUST NOT wait for a reply without a timeout.

## ACTV and ANOF (S->C)

ACTV asks the client to bring its window to the front, or to open a tool. ANOF is the same request with no focus change.

- Direction: S->C
- Version: 212
- Module: vconcomm

| Trigger                                          | Packet                        | Payload     |
| ------------------------------------------------ | ----------------------------- | ----------- |
| Console command `showconsole` or `toggleconsole` | ACTV                          | Empty.      |
| Console command `consoletool <toolname> [args]`  | ACTV, or ANOF with `-nofocus` | Tool block. |
| Console command `bug` or `auto_bug`              | ACTV                          | Tool block. |

A tool block is a KeyValues1 text block named after the tool. If the command text is not empty, `arg0` is the tool name. `arg1` to `argN` are the words of the text after `consoletool`, split like a command line. Thus `consoletool Foo a b` gives `arg0` Foo, `arg1` Foo, `arg2` a, `arg3` b. For `bug`, the arguments are the command arguments and `-noscreenshot`. If one argument is `-nofocus` (any case), the server sends ANOF and keeps `-nofocus` in the block. Otherwise, the server sends ACTV.

Without `-nofocus`, the server first sends AINF with the game window handle to all clients. If a client is connected from 127.0.0.1 and a VConsole2 window is visible on the machine, the server brings that window to the front. It then sends an empty ACTV to all clients. Otherwise, it starts VConsole2 and sends no empty ACTV. `consoletool` then sends the tool block, so a local client gets an empty ACTV and then the block.

With `-nofocus`, the server sends AINF, and then ANOF if a VConsole2 window is visible.

## ACTV (C->S)

The client gives its window handle to the server. The handler sets that window as the active window. If the cvar `sv_unpause_on_console_close` is not 0, the handler also runs `unpause bug;unpause`.

- Direction: C->S
- Version: 212 (the handler registers version 0 and checks 212)
- Module: engine2

| Offset | Size | Type   | Name | Meaning                                     |
| ------ | ---- | ------ | ---- | ------------------------------------------- |
| 0      | 8    | u64 LE | HWND | Window handle of the client. Little-endian. |

A client that is not a window on the same machine SHOULD NOT send ACTV.

## EXIT

Asks the client to close. No server sends EXIT. The server sends CMND `vc_exit <context>` instead.

- Direction: S->C
- Version: 212
- Module: none (client only)

The payload is empty.

A client MAY close on EXIT.

## VFCS

The focus state of the client window. The server stores the last Focus from any client and treats it as "the console has focus". It resets the value to 0 when no client is connected. The VConsole2 client sends VFCS only when it connects to 127.0.0.1, and stops when AINF has flag 0x10.

- Direction: C->S
- Version: 212
- Module: vconcomm

| Offset | Size | Type | Name  | Meaning                                       |
| ------ | ---- | ---- | ----- | --------------------------------------------- |
| 0      | 1    | u8   | Focus | Not 0 means that the client window has focus. |

## USRN

The user name of the client. The server stores one user name for each client IP address, so clients from the same address share it. A dedicated server shows the name in its command log line ([CMND processing](#cmnd-processing)).

- Direction: C->S
- Version: 212
- Module: vconcomm

| Offset | Size | Type | Name | Meaning    |
| ------ | ---- | ---- | ---- | ---------- |
| 0      | var  | cstr | Name | User name. |

A client MAY send USRN at any time.

## PING

A connection check. The server replies with PONG on the main thread, in the same step as CMND. It does not reply while the main thread processes no input frames, for example during startup and long loads. A missing PONG means that the game is busy or that the connection is dead. For the client rules, see [Client slots](transport.md#client-slots).

- Direction: C->S
- Version: 1
- Module: vconcomm

| Offset | Size | Type  | Name | Meaning    |
| ------ | ---- | ----- | ---- | ---------- |
| 0      | var  | bytes | Data | Any bytes. |

## PONG

The reply to PING.

- Direction: S->C
- Version: 1
- Module: vconcomm

| Offset | Size | Type  | Name | Meaning                         |
| ------ | ---- | ----- | ---- | ------------------------------- |
| 0      | var  | bytes | Data | Exact copy of the PING payload. |

Each client gets the PONG for the PING of every client. A client SHOULD put a unique value in each PING payload and match the PONG by that value.

## Command execution and output

### CMND processing

The engine runs the CMND text on the main thread as console input, with the normal console rules. For example, a cvar with the cheat flag needs `sv_cheats 1`. On a dedicated server, the engine first logs `>>> ip: command` on the `General` channel, or `>>> name@ip: command` when the client IP has a [USRN](#usrn) name. On a game client, a filter can drop some text.

A client MUST end the payload of CMND, USRN and AUTO Text with a NUL byte. The server adds no NUL. Each command in the text, split at `;` or a newline, MUST be shorter than 511 bytes. The server does not run a longer command and logs "WARNING: Command too long... ignoring!".

### Hidden commands

The game can hide commands and cvars. A CMND for a hidden command does nothing and produces no PRNT. The CVRB list leaves out hidden entries. With `-tools`, the game shows some hidden entries. A client MUST NOT expect an error message for a command that does not run.

### Output correlation

Output has no request ID, no reply packet and no end marker. Output arrives as ordinary PRNT packets on any channel. Other log lines can arrive between output lines. Each client also gets the output of commands from other clients.

A client SHOULD find the end of the output with an echo marker:

1. Make a unique marker text, for example `__vc_end_1234__`.
2. Send CMND with the command.
3. Send CMND with `echo <marker>`.
4. Collect all PRNT packets after step 2.
5. Stop at the PRNT on the `Console` channel (`0x094098AD`) whose Text is the marker followed by `\n`.
6. Use a timeout for step 5.

The command buffer runs commands in the sequence that it gets them. Thus, the marker line follows the immediate output of the command. A client MAY put both commands in one CMND, separated by `;`. A client MUST NOT combine them with `;` when the command text has an unclosed quote.

The output of `echo` has these PRNT values: ChannelID `0x094098AD`, Severity 2, color 0 (channel color).

A client SHOULD discard PRNT packets that arrive before step 2. These packets include the history replay ([History replay](transport.md#history-replay)).

### Late output

Some commands start work that ends in a later frame. Their output arrives after the marker line. Examples are map loads, screenshot writes and other jobs that run over many frames. A client SHOULD continue to read PRNT packets after the marker when it expects late output. A client MAY stop after a quiet interval.
