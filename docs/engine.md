# Engine packets

The modules `engine2` and `client` send and receive the packets on this page.

## FRMS

Frame time statistics. The engine sends FRMS once per frame while all these conditions are true:

- The cvar `stats_display` is neither 0 nor 5. Its default is 0.
- No benchmark runs.
- At least one client is connected.

Packet properties:

- Direction: S->C
- Version: 2
- Module: engine2

| Offset | Size | Type   | Name         | Meaning                                                    |
| ------ | ---- | ------ | ------------ | ---------------------------------------------------------- |
| 0      | 4    | f32 LE | AverageTime  | Average frame time in seconds, over the last 30 frames.    |
| 4      | 4    | f32 LE | MaxDeviation | Largest difference between one frame time and the average. |
| 8      | 4    | f32 LE | FPS          | Frames per second (1 / AverageTime).                       |
| 12     | var  | cstr   | Text         | The scene system statistics report.                        |

Text is a human-readable report. The cvar `sc_stats_sections` selects its sections. A client SHOULD NOT parse it.

## Bug reports

### BUGI

Bug report data. The console commands `bug` and `auto_bug` open the bug reporter tool ([ACTV and ANOF (S->C)](console.md#actv-and-anof-s-c)). The client sends BUGI. The server then sends a sequence of BUGI packets, one for each attachment.

- Direction: C->S and S->C
- Version: 3
- Module: engine2

The server does not read the C->S payload. A client SHOULD send an empty payload.

S->C payload:

| Offset | Size | Type     | Name     | Meaning                                             |
| ------ | ---- | -------- | -------- | --------------------------------------------------- |
| 0      | 32   | char[32] | Title    | Attachment title. Empty for the metadata block.     |
| 32     | 64   | char[64] | FileName | Attachment file name. Empty for the metadata block. |
| 96     | 4    | u32 LE   | IsText   | 1 if Data is text. 0 if Data is binary.             |
| 100    | var  | bytes    | Data     | Attachment data.                                    |

The server sends the attachments in this sequence:

| Title           | FileName          | Data                                   |
| --------------- | ----------------- | -------------------------------------- |
| `ConVars`       | `convars.txt`     | KeyValues1 text of the cvars.          |
| Set by the game | Set by the game   | Client state blocks, one packet each.  |
| Set by the game | Set by the game   | More state blocks, one packet each.    |
| `Input State`   | `input_state.txt` | Input state as log text.               |
| `Sound State`   | `sound_state.txt` | Sound system state.                    |
| `Annotation`    | `<name>.wav`      | Voice attachment, only if it exists.   |
| empty           | empty             | KeyValues1 text with the bug metadata. |

The metadata block has the keys `bug_submitter_override`, `position`, `angle`, `build`, `map`, `savefile`, `built_from_cl`, `retail` and more.

### BUGS

A screenshot for the bug reporter. The C->S packet queues a screenshot request. The server sends BUGS with the image data when the screenshot is ready.

- Direction: C->S and S->C
- Version: 3
- Module: engine2

C->S payload:

| Offset | Size | Type   | Name      | Meaning                                                                                   |
| ------ | ---- | ------ | --------- | ----------------------------------------------------------------------------------------- |
| 0      | 8    | u64 LE | RequestID | A number that the client chooses. The server stores it and returns it in the S->C packet. |

S->C payload:

| Offset | Size | Type     | Name          | Meaning                                                                                    |
| ------ | ---- | -------- | ------------- | ------------------------------------------------------------------------------------------ |
| 0      | 8    | u64 LE   | RequestID     | The RequestID of the C->S packet. It is 0 when the server requested the screenshot itself. |
| 8      | 4    | bytes[4] | Uninitialized | Any value.                                                                                 |
| 12     | var  | bytes    | Data          | JPEG image (JFIF). The size is Length - 24.                                                |

### BUGO and BUGC

BUGO sets the "bug reporter open" flag of the server. BUGC clears it. When the flag has the value 1 and no client has a connection, the server clears the flag and runs `unpause bug`.

- Direction: C->S
- Version: 3
- Module: engine2

The server does not read the payload. A client SHOULD send an empty payload.

## VProf

### VPRF

Starts or stops the VProf stream (PCAT, PCFG, PCPU, PTAT, PTEX). The engine applies the request at the next frame and logs "VProf Enabled." or "VProf Disabled.". The console commands `vprof_on` and `vprof_off` make the same request.

- Direction: C->S
- Version: 3
- Module: engine2

| Offset | Size | Type | Name       | Meaning                                                                                                         |
| ------ | ---- | ---- | ---------- | --------------------------------------------------------------------------------------------------------------- |
| 0      | 1    | u8   | Enable     | 0 stops VProf. Other values start it.                                                                           |
| 1      | 1    | u8   | BudgetMode | If 1, the server does not calculate budget group times.                                                         |
| 2      | 1    | u8   | CounterSet | 0 = none, 1 = global texture counters (`texgroup_global_`), 2 = per-frame texture counters (`texgroup_frame_`). |
| 3      | 1    | u8   | AllThreads | Not 0 = profile all threads. Same flag as the cvar `vprof_all_threads`.                                         |

The server reads bytes 1 to 3 only when Enable is not 0. The stream also needs a connected client.

### VMPN and VMPC

- Direction: C->S
- Version: 3
- Module: engine2

The server rejects these tags ([Version](transport.md#version)). A client MUST NOT send VMPN or VMPC.

### PCAT

The VProf budget group list. The server sends it when the stream starts. PCFG follows.

- Direction: S->C
- Version: 3
- Module: engine2

| Offset | Size       | Type   | Name    | Meaning           |
| ------ | ---------- | ------ | ------- | ----------------- |
| 0      | 4          | u32 BE | Count   | Number of groups. |
| 4      | 68 x Count | entry  | Entries | Group entries.    |

Entry (68 bytes):

| Offset | Size | Type     | Name | Meaning            |
| ------ | ---- | -------- | ---- | ------------------ |
| 0      | 64   | char[64] | Name | Budget group name. |
| 64     | 1    | u8       | R    | Red.               |
| 65     | 1    | u8       | G    | Green.             |
| 66     | 1    | u8       | B    | Blue.              |
| 67     | 1    | u8       | A    | Alpha.             |

### PCFG

The VProf "profile all threads" flag. The server sends it after PCAT, and when the flag changes.

- Direction: S->C
- Version: 3
- Module: engine2

| Offset | Size | Type | Name       | Meaning                      |
| ------ | ---- | ---- | ---------- | ---------------------------- |
| 0      | 1    | u8   | AllThreads | Not 0 = profile all threads. |

### PCPU

VProf budget group times for one frame. The server sends it each frame while VProf runs and a client has a connection.

- Direction: S->C
- Version: 3
- Module: engine2

| Offset | Size      | Type   | Name       | Meaning                                                                                       |
| ------ | --------- | ------ | ---------- | --------------------------------------------------------------------------------------------- |
| 0      | 4         | u32 BE | Count      | Number of groups, 256 or fewer.                                                               |
| 4      | 4         | u32 BE | Counter    | Number of server ticks that ran since the last PCPU. The server sets it to 0 after each send. |
| 8      | 4         | f32 BE | FrameTime  | Frame rate in frames per second. The value is a rate, not a time.                             |
| 12     | 4 x Count | u32 BE | GroupTimes | Time of each budget group, in the PCAT sequence. The unit is not defined.                     |

### PTAT

The names of the texture counters in the selected counter set (VPRF CounterSet). The server sends it when the stream starts.

- Direction: S->C
- Version: 3
- Module: engine2

The layout is the same as PCAT. Name is the counter name without the prefix `texgroup_frame_` or `texgroup_global_`. The color is the color of the budget group with the same index.

### PTEX

The values of the texture counters for one frame.

- Direction: S->C
- Version: 3
- Module: engine2

| Offset | Size        | Type   | Name   | Meaning                                                    |
| ------ | ----------- | ------ | ------ | ---------------------------------------------------------- |
| 0      | 4           | u32 BE | N      | Number of values plus 1.                                   |
| 4      | 4 x (N - 1) | i32 BE | Values | Counter values, in the PTAT sequence. 255 values or fewer. |

## Entity report

### EFUL

The game client received a full entity update from the game server. A full update is an entity message that is not a delta. The engine sends EFUL to all clients, whether or not the entity report is on (ENTC). A dedicated server never sends it.

- Direction: S->C
- Version: 5
- Module: engine2

The payload is empty.

The engine sends EFUL before it reads the entities of the full update. The ENTR packet of the same update comes after EFUL and has an add event for each entity. A client SHOULD clear its entity list on EFUL and rebuild it from the next ENTR.

No packet requests EFUL.

### ENTC

Starts or stops the entity report. The game client registers the handler. On a dedicated server, the network server service registers it. After ENTC with `enable` true, the game sends ENTR, ECHG and EPLY. ENTC with `enable` false clears the `objects` list.

- Direction: C->S
- Version: 5
- Module: engine2

Payload: one KV3 object.

| Key     | Type         | Meaning                                                                                                                           |
| ------- | ------------ | --------------------------------------------------------------------------------------------------------------------------------- |
| enable  | bool         | True starts the report. False stops it.                                                                                           |
| objects | array of int | Entity indices. The game sends ECHG only for these entities. An empty array stops all ECHG packets. ENTR and EPLY do not use it.  |
| recip   | int          | Recipient filter for ECHG. The value -1 means no filter. The server stores it. The game client ignores it. A tool SHOULD send -1. |

### ENTR

The entity report. The game sends one ENTR packet at the end of each PacketEntities message, only while the report is on and a client has a connection. The packet contains the events of that message.

- Direction: S->C
- Version: 5
- Module: engine2

Payload: one KV3 object with one key, `reports`, an array of objects.

| Key        | Type   | Meaning                                                                             |
| ---------- | ------ | ----------------------------------------------------------------------------------- |
| type       | string | Event type. See the next table.                                                     |
| entindex   | int    | Entity index.                                                                       |
| bitcount   | int    | Number of bits in the update. The value is 0 for types other than `ERD_RECORDBITS`. |
| fieldcount | int    | Number of changed fields. The value is 0 for types other than `ERD_RECORDBITS`.     |
| class      | string | Class name. The text can be empty.                                                  |
| dormant    | bool   | The entity is dormant.                                                              |
| inpvs      | bool   | The entity is in the PVS.                                                           |

| Type             | dormant | inpvs | Meaning                                                                                            |
| ---------------- | ------- | ----- | -------------------------------------------------------------------------------------------------- |
| `ERD_RECORDBITS` | false   | true  | The game read or wrote an update of an existing entity. `bitcount` and `fieldcount` give its size. |
| `ERD_ADD`        | false   | true  | The game created the entity from a packet.                                                         |
| `ERD_LEAVEPVS`   | true    | false | The entity left the PVS. The game keeps the entity.                                                |
| `ERD_DELETE`     | false   | false | The game removed the entity.                                                                       |

### ECHG

One networked field of an entity that the entity report watches. The game sends one ECHG packet each time the serializer reads a field (game client) or writes a field (game server). The game sends it only for the entities in the ENTC `objects` list.

- Direction: S->C
- Version: 5
- Module: engine2

Payload: one KV3 object.

| Key        | Type   | Meaning                              |
| ---------- | ------ | ------------------------------------ |
| objectid   | int    | Entity or object ID.                 |
| recip      | int    | Recipient.                           |
| bits       | int    | Number of bits.                      |
| finish     | bool   | Reserved. The value is always false. |
| type       | string | Field type.                          |
| path       | string | Field path.                          |
| shortname  | string | Short field name.                    |
| fullpath   | string | Full field path.                     |
| usergroup  | string | User group.                          |
| value      | string | Field value as text.                 |
| serializer | string | Serializer name.                     |
| encoder    | string | Encoder name.                        |
| sendnode   | string | Send node.                           |
| notes      | string | Notes.                               |
| is_nullptr | bool   | The `value` text is empty.           |

### EPLY

The player slot list. The network server service sends it when the entity report is on and the list changed. The service checks the list at most once every 2 seconds. The first check runs when ENTC enables the report.

- Direction: S->C
- Version: 5
- Module: engine2

Payload: one KV3 object.

| Key          | Type             | Meaning                                                           |
| ------------ | ---------------- | ----------------------------------------------------------------- |
| slots        | array of objects | One object for each player slot.                                  |
| slots[].slot | int              | Player slot.                                                      |
| slots[].name | string           | Player name.                                                      |
| reset        | bool             | Always true. The list is complete and replaces the previous list. |
| dedicated    | bool             | Always true.                                                      |

## Prediction

### PRDC

Starts or stops the prediction report. If `highest_string` is larger than the number of strings that the game sent, the game clears its string table and starts again.

- Direction: C->S
- Version: 1
- Module: client

Payload: one KV3 object.

| Key            | Type | Meaning                                            |
| -------------- | ---- | -------------------------------------------------- |
| enable         | bool | True starts the report. False stops it.            |
| highest_string | int  | The highest global string index that the tool has. |

### PRHE

The header of one prediction report. The game sends it after each network update while the report is on and a client has a connection. PRED packets follow, one for each entity.

- Direction: S->C
- Version: 1
- Module: client

Payload: one KV3 object.

| Key                | Type            | Meaning                                        |
| ------------------ | --------------- | ---------------------------------------------- |
| report_counter     | int32           | Report number.                                 |
| entities           | array of int32  | Entity indices in this report.                 |
| reset              | bool            | The client MUST clear its string table.        |
| global_start_index | int32           | Index of the first string in `global_strings`. |
| global_strings     | array of string | New global strings.                            |

### PRED

The prediction report for one entity.

- Direction: S->C
- Version: 1
- Module: client

Payload: one KV3 object. The tables show the integer types.

| Key                    | Type             | Meaning                                               |
| ---------------------- | ---------------- | ----------------------------------------------------- |
| entity_index           | int32            | Entity index.                                         |
| owner_entity_index     | int32            | Owner entity index.                                   |
| size                   | int32            | Size of the prediction data.                          |
| intermediate_size      | int32            | Size of the intermediate data.                        |
| classname_global_index | int32            | Class name, as an index into the global strings.      |
| local_strings          | array of string  | Strings for this entity only.                         |
| predictable            | bool             | The entity is predictable.                            |
| added                  | array of objects | New fields. See the next table.                       |
| updated                | array of objects | Changed fields. See the next table.                   |
| removed                | array of uint32  | The `hash` values of the fields that left the report. |

| Element   | Keys                                                                                                                                                                   |
| --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `added`   | int32: classname_global_index, fieldname_global_index, fieldtype_global_index, fieldnotes_global_index, fieldpath_local_index, value_local_index. uint32: flags, hash. |
| `updated` | int32: value_local_index. uint32: hash, flags.                                                                                                                         |
