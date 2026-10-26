# Resource system packets

## RCMD

Turns the resource profile mode on or off. While the mode is on, the server sends RSPF at the end of each resource system frame.

- Direction: C->S
- Version: 7

Payload: one KV3 object.

| Key              | Type   | Meaning                                     |
| ---------------- | ------ | ------------------------------------------- |
| command          | string | MUST be `set_profile_mode`.                 |
| enable_profiling | bool   | True turns the mode on. False turns it off. |

## RSPF

Resource load and frame timing records. The server sends one RSPF at the end of each resource system frame while the profile mode is on. An RCMD request takes effect at the start of the next frame. Each packet holds a frame start record and a frame end record (kind 0, Type 0). The frame end record is always the last record. The frame start record is usually the first record. Records of kinds 1, 4, 5 and 6, and kind 0 records of Types 2 and 25, can come before it. The frame numbers of two consecutive packets can differ by more than 1.

- Direction: S->C
- Version: 7

All fields are little-endian. The server does not initialize padding bytes or fields marked "Not initialized". A client MUST ignore them.

| Offset | Size | Type   | Name      | Meaning                                                                                                                                                                                         |
| ------ | ---- | ------ | --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 4    | u32    | Version   | Always 7.                                                                                                                                                                                       |
| 4      | 4    | u32    | ProcessID | Process ID of the game.                                                                                                                                                                         |
| 8      | 4    | u32    | Sequence  | Starts at 0 and increases by 1 for each packet that the server builds. A packet that the server drops also uses a number. Thus the first packet that a client gets can have a Sequence above 0. |
| 12     | 4    | u32    | Count     | Number of records.                                                                                                                                                                              |
| 16     | var  | record | Records   | Count records, back to back with no alignment.                                                                                                                                                  |

The server drops a packet that is too large ([Oversize packets](errors.md#oversize-packets)). The first RSPF packets can be too large.

Record frame:

| Offset | Size | Type  | Name    | Meaning                                            |
| ------ | ---- | ----- | ------- | -------------------------------------------------- |
| 0      | 2    | u16   | Kind    | Record kind, 0 to 8.                               |
| 2      | 2    | u16   | Size    | Payload size in bytes. Each kind has a fixed size. |
| 4      | Size | bytes | Payload | Payload of the kind.                               |

| Kind | Size | Payload                      |
| ---- | ---- | ---------------------------- |
| 0    | 288  | Resource system event.       |
| 1    | 272  | Resource binding created.    |
| 2    | 24   | Resource request processed.  |
| 3    | 40   | Resource binding load event. |
| 4    | 516  | Entity spawn.                |
| 5    | 24   | Entity event.                |
| 6    | 272  | Manifest created.            |
| 7    | 24   | Manifest state change.       |
| 8    | 280  | App system event.            |

Each Time is in microseconds since the process started. It uses the same clock as the PRNT Time, which is in milliseconds.

Kind 0, resource system event:

| Offset | Size | Type      | Name  | Meaning                                                                                                                                                                                                   |
| ------ | ---- | --------- | ----- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 4    | u32       | Type  | 0 = frame. 1 = blocking manifest load. 2 = update of one resource type manager. 25 = the game systems handle the start of a spawn group load. 26 = the game systems handle the end of a spawn group load. |
| 4      | 4    | u32       | Phase | 1 = start. 2 = end.                                                                                                                                                                                       |
| 8      | 8    | u64       | Time  | Time.                                                                                                                                                                                                     |
| 16     | 4    | u32       | ID    | Types 0, 25 and 26: the resource system frame number. Type 1: 0. Type 2: a counter that the start and the end share. The counter starts at 0 when the profile mode turns on.                              |
| 20     | 260  | char[260] | Name  | Type 1 start: the manifest name, 256 bytes or fewer. All other records: empty.                                                                                                                            |
| 280    | 8    | u64       | Value | Types 0, 25 and 26: 0. Type 1: a counter of blocking loads. Type 2: the resource type of the manager as ASCII text in the low bytes, for example `0x78657476` for `vtex`.                                 |

Types 25 and 26 occur in pairs of start and end records during a map load. The client and the server game modules write them.

Kind 1, resource binding created:

| Offset | Size | Type      | Name       | Meaning                     |
| ------ | ---- | --------- | ---------- | --------------------------- |
| 0      | 8    | u64       | ResourceID | Resource ID of the binding. |
| 8      | 256  | char[256] | Name       | Resource name.              |
| 264    | 8    | u64       | Time       | Time.                       |

Kind 2, resource request processed:

| Offset | Size | Type | Name       | Meaning                                                                                                            |
| ------ | ---- | ---- | ---------- | ------------------------------------------------------------------------------------------------------------------ |
| 0      | 8    | u64  | ParentID   | Resource ID of the resource that made the request, or 0.                                                           |
| 8      | 8    | u64  | ResourceID | Resource ID of the requested resource, as in kind 1. The server computes it as a 64-bit hash of the resource name. |
| 16     | 4    | u32  | OwnerID    | ID of the manifest of the request, or 0xFFFFFFFF.                                                                  |
| 20     | 4    | -    | -          | Not initialized.                                                                                                   |

Kind 3, resource binding load event:

| Offset | Size | Type | Name       | Meaning                                                                                                                   |
| ------ | ---- | ---- | ---------- | ------------------------------------------------------------------------------------------------------------------------- |
| 0      | 8    | u64  | ResourceID | Resource ID of the binding.                                                                                               |
| 8      | 4    | u32  | Event      | Event code. See the next table.                                                                                           |
| 12     | 4    | u32  | Phase      | 0 = instant. 1 = start. 2 = end.                                                                                          |
| 16     | 8    | u64  | Time       | Time.                                                                                                                     |
| 24     | 8    | u64  | Extra      | Event 6: a file system read statistic. Event 15: a value from 1 to 10. Events 3 and 10: 0. Other events: not initialized. |
| 32     | 4    | u32  | OwnerID    | ID of the manifest (kind 6), or 0xFFFFFFFF.                                                                               |
| 36     | 4    | u32  | Extra2     | Event 6: a file system read statistic. Events 3, 10 and 15: 0. Other events: not initialized.                             |

| Event | Phase | Meaning                                                                                                                                                                                                        |
| ----- | ----- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 3     | 0     | The server queues the data request. All resource types.                                                                                                                                                        |
| 6     | 1, 2  | The file read. The start and end times come from the file system. Extra and Extra2 come from the file system read statistics.                                                                                  |
| 7     | 0     | The data request is complete. All resource types.                                                                                                                                                              |
| 10    | 1, 2  | The resource type manager processes the data on a worker thread. The start and end times come from the loader. Some resource types, for example models, materials, particles, animation clips and world files. |
| 11    | 1, 2  | The resource type manager creates the resource from the data. All resource types.                                                                                                                              |
| 12    | 1, 2  | The resource type manager completes the resource after its references resolve. Some resource types, for example models, sounds, particles, vector images and data files.                                       |
| 15    | 0     | The load succeeds. All resource types.                                                                                                                                                                         |
| 16    | 0     | The load fails. The server also logs "Failed loading resource".                                                                                                                                                |

For each resource, the server writes the records in this sequence: kind 1, event 3, event 10, event 7, event 11, event 15, event 12. Events 10 and 12 occur only for some resource types. The record sequence is not a time sequence. An event 10 record can have a Time earlier than the kind 1 record of the same resource.

Kind 4, entity spawn. The client and the server game modules write one record for each entity immediately before the entity spawns:

| Offset | Size | Type      | Name      | Meaning                                                                                                                                                                                |
| ------ | ---- | --------- | --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 4    | u32       | Entity    | Entity handle. The low 15 bits are the entity index. The bits above them are the serial number. For example, `0x8000` is the world entity. 0xFFFFFFFF when the entity has no identity. |
| 4      | 256  | char[256] | Name      | Entity name (targetname), or empty.                                                                                                                                                    |
| 260    | 256  | char[256] | ClassName | Entity class name.                                                                                                                                                                     |

Kind 5, entity event. The client and the server game modules write a start record and an end record around each entity event:

| Offset | Size | Type | Name   | Meaning                                                                                            |
| ------ | ---- | ---- | ------ | -------------------------------------------------------------------------------------------------- |
| 0      | 4    | u32  | Entity | Entity handle, as in kind 4.                                                                       |
| 4      | 4    | u32  | Code   | 21 = spawn. 22 = activate. 23 = remove. Each kind 4 record comes with one pair of code 21 records. |
| 8      | 4    | u32  | Phase  | 1 = start. 2 = end.                                                                                |
| 12     | 4    | u32  | Frame  | Resource system frame number, as in kind 0.                                                        |
| 16     | 8    | u64  | Time   | Time.                                                                                              |

Kind 6, manifest created:

| Offset | Size | Type      | Name       | Meaning          |
| ------ | ---- | --------- | ---------- | ---------------- |
| 0      | 4    | u32       | ManifestID | Manifest ID.     |
| 4      | 4    | -         | -          | Not initialized. |
| 8      | 8    | u64       | Time       | Time.            |
| 16     | 256  | char[256] | Name       | Manifest name.   |

Kind 7, manifest state change:

| Offset | Size | Type | Name       | Meaning                                                     |
| ------ | ---- | ---- | ---------- | ----------------------------------------------------------- |
| 0      | 4    | u32  | ManifestID | Manifest ID, as in kind 6.                                  |
| 4      | 4    | u32  | Code       | 19 = a blocking load starts. 20 = the manifest is complete. |
| 8      | 4    | u32  | Zero       | Always 0.                                                   |
| 12     | 4    | -    | -          | Not initialized.                                            |
| 16     | 8    | u64  | Time       | Time.                                                       |

Kind 8, app system event:

| Offset | Size | Type      | Name  | Meaning                                                                                                                                                |
| ------ | ---- | --------- | ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 0      | 4    | u32       | Type  | 27 = the engine loads a module. 28 = the engine connects an app system. 29 = the engine initializes an app system.                                     |
| 4      | 4    | u32       | Phase | 1 = start. 2 = end.                                                                                                                                    |
| 8      | 8    | u64       | ID    | A counter that the start and the end share. It starts at 10000 and increases by 1 for each start.                                                      |
| 16     | 8    | u64       | Time  | Time.                                                                                                                                                  |
| 24     | 256  | char[256] | Name  | Start: the path of the module for type 27, or the name of the app system for types 28 and 29. End: empty. The bytes after the NUL are not initialized. |

A client matches the start and end records by ID. While the profile mode is off, the server sends no RSPF but keeps the kind 8 records. When the mode turns on, the first RSPF packet contains these kind 8 records before its frame start record.
