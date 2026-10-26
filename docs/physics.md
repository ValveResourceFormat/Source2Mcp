# Physics packets

All multi-byte fields on this page are little-endian.

## PHRB

Physics Debugger requests. The payload is a list of 8-byte data headers. Byte 0 of a header is the command. Byte 1 is a flag for some commands. Bytes 4 to 7 are a u32 count or size for some commands.

- Direction: C->S
- Version: 2

| Command        | Header and effect                                                                                                                                                                                                                                                                                            |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 0              | The server replies with the tick frequency (PHRA type 0). No data.                                                                                                                                                                                                                                           |
| 1              | Sets state bit `0x2` in the physics debugger object.                                                                                                                                                                                                                                                         |
| 2              | Sets or clears state bit `0x4`. The flag is bit 0 of byte 4.                                                                                                                                                                                                                                                 |
| 3              | Sets or clears state bit `0x8`. The flag is bit 0 of byte 4.                                                                                                                                                                                                                                                 |
| 4              | With no more data, sets state bit `0x10`. With data, one u64 object pointer and one u64 value follow. The server stores the value in the world of that pointer. If the pointer is not a known world, the server does not skip the u64 value. It reads that value as the next data header.                    |
| 5              | One u64 object pointer follows. The server replies with PHRA type 5 (debug name).                                                                                                                                                                                                                            |
| 6, 7           | The server builds a PHRA reply of the same type number.                                                                                                                                                                                                                                                      |
| 8              | Sets the debug draw filter of each named world. Bytes 4 to 7 hold the size, counted from the start of the header. Records follow. Each record is a u64 world pointer and a draw filter. The server ignores a record for an unknown world.                                                                    |
| 9              | Clears the debug draw filter of every world. No data.                                                                                                                                                                                                                                                        |
| 10             | Sets the debug draw filter by world kind. Bytes 4 to 7 hold a world mask. One draw filter follows. A world gets the filter when the mask and its create flags have a common bit, or when the mask has bit `0x8` and the create flags have none of the bits `0x7`. All other worlds get their filter cleared. |
| 12             | Byte 1 sets or clears state bit `0x1`. Bit 1 of byte 1 selects which code sends the per-world messages: the per-tick update when clear, the world step when set. Bytes 4 to 7 replace the next stream ID.                                                                                                    |
| 13             | Sets the selection weight of the shapes in one world. Bytes 4 to 7 hold a count. A u64 world pointer follows, then count items of a u32 LE shape ID and an f32 LE weight, sorted by shape ID. The server gives each shape the weight of the item with its shape ID, or 0.0 when no item matches.             |
| 14             | Sets or clears state bit `0x4000` from bit 0 of byte 4.                                                                                                                                                                                                                                                      |
| 15             | Byte 1 sets or clears state flag `0x20`.                                                                                                                                                                                                                                                                     |
| 18, 19, 20, 21 | Byte 1 sets or clears state flags `0x2`, `0x4`, `0x8` and `0x10`. Command 21 also copies the flags to every world.                                                                                                                                                                                           |
| other          | The server ignores the command.                                                                                                                                                                                                                                                                              |

A shape ID is a 28-bit number from a counter that all worlds share. The server gives a shape a new shape ID when it creates the shape and each time it registers the shape again. Command 13, the hidden IDs of a draw filter and the PartnerShape field of a draw filter use shape IDs. These fields carry shape IDs only from the client to the server. No server message carries shape IDs, so a client can only use shape IDs that it gets in some other way.

The game creates its physics worlds with these create flags:

| Create flags | World                                                                                 |
| ------------ | ------------------------------------------------------------------------------------- |
| 0x1          | The client game world.                                                                |
| 0x2          | The server game world.                                                                |
| 0x9          | Each other client world, for example the engine time world and the per-object worlds. |
| 0xA          | Each other server world.                                                              |
| 0x4          | Some tool worlds.                                                                     |
| 0x8          | A tool world for mass calculation.                                                    |
| 0            | All other tool worlds.                                                                |

Bit 0x8 marks a world that is not the main game world of its side. No game world has bit 0x4, so a mask with only bit 0x4 selects only tool worlds. A world with create flags 0 matches only a mask with bit 0x8. The default mask 0x7 does not select it.

A draw filter has 260 fixed bytes, followed by three u32 LE arrays. The arrays follow in this order: surface hashes (count at offset 52), hidden IDs (count at offset 60), material hashes (count at offset 56). The size is 260 + 4 x (sum of the counts). A size mismatch is a [fatal error](errors.md#fatal-error-packets). The server searches each array with a binary search, so a client MUST sort each array in ascending order.

The server skips a test on a mask field when the field is 0. It also skips a test on a u64 mask when the mask has all bits set.

| Offset | Size | Type     | Name                 | Meaning                                                                                                                                                                                                   |
| ------ | ---- | -------- | -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 8    | u64      | CollisionGroups      | Bit mask of collision group indices. A shape passes when the bit of its collision group is set.                                                                                                           |
| 8      | 8    | u64      | InteractAs           | A shape passes when its interacts-as layers have a common bit with this mask.                                                                                                                             |
| 16     | 8    | u64      | InteractWith         | A shape passes when its interacts-with layers have a common bit with this mask.                                                                                                                           |
| 24     | 8    | u64      | InteractExclude      | A shape passes when its interacts-exclude layers have a common bit with this mask.                                                                                                                        |
| 32     | 2    | u16      | TargetDetailLayers   | A shape passes when the bit of its target detail layer is set.                                                                                                                                            |
| 34     | 2    | u16      | IncludedDetailLayers | A shape passes when its included detail layers have a common bit with this mask.                                                                                                                          |
| 36     | 16   | bytes    | DrawMask             | Debug draw flags. The server copies all 16 bytes into the debug draw flags of the world.                                                                                                                  |
| 52     | 4    | u32      | SurfaceCount         | Number of surface hashes. A shape passes when its surface property or one of its triangle materials is in the list.                                                                                       |
| 56     | 4    | u32      | MaterialCount        | Number of material hashes.                                                                                                                                                                                |
| 60     | 4    | u32      | HiddenCount          | Number of hidden IDs. The server never draws an object whose ID is in the list. A shape uses its shape ID. A recorded query uses its ID with 0xD in the top 4 bits and the query type in the next 4 bits. |
| 64     | 1    | u8       | Active               | 1 when the filter applies. When it is 0, the server draws without the filter.                                                                                                                             |
| 65     | 1    | u8       | DrawStyle            | 0 draws nothing. 1 = wireframe. 2 = solid. 3 = transparent. 4 = plain colors.                                                                                                                             |
| 66     | 1    | u8       | Invert               | When not 0, the server inverts the result of each test except the hidden ID test.                                                                                                                         |
| 67     | 1    | u8       | Alpha                | Opacity for draw style 3, 0 to 255.                                                                                                                                                                       |
| 68     | 1    | u8       | CollisionFunctions   | A shape passes when its collision function mask has a common bit with this mask. 0xFF disables the test.                                                                                                  |
| 69     | 64   | char[64] | DebugName            | The server draws only bodies whose debug name contains this text, case-insensitive. The server sets byte 132 to 0.                                                                                        |
| 133    | 64   | char[64] | StackFilter          | The server draws only recorded queries whose call stack strings contain this text, case-insensitive. The server sets byte 196 to 0.                                                                       |
| 197    | 4    | u32      | PartnerShape         | Shape ID of a reference shape.                                                                                                                                                                            |
| 201    | 2    | u16      | PartnerFunctions     | When not 0, the server draws only shapes that collide with the reference shape. The collision functions of the pair must have a common bit with this mask.                                                |
| 203    | 1    | u8       | QueryEnabled         | When not 0, the server draws only shapes that a query with QueryAttributes would hit.                                                                                                                     |
| 204    | 56   | struct   | QueryAttributes      | Query attributes. See the next table.                                                                                                                                                                     |

Query attributes (offsets from filter byte 204):

| Offset | Size | Type   | Name                 | Meaning                               |
| ------ | ---- | ------ | -------------------- | ------------------------------------- |
| 0      | 8    | u64    | InteractsWith        | Layers that the query interacts with. |
| 8      | 8    | u64    | InteractsExclude     | Layers that the query excludes.       |
| 16     | 8    | u64    | InteractsAs          | Layers that the query represents.     |
| 24     | 8    | u32[2] | EntityIdsToIgnore    | 0xFFFFFFFF when not used.             |
| 32     | 8    | u32[2] | OwnerIdsToIgnore     | 0xFFFFFFFF when not used.             |
| 40     | 4    | u16[2] | HierarchyIds         | Hierarchy IDs.                        |
| 44     | 2    | u16    | IncludedDetailLayers | Default 0xFFFF.                       |
| 46     | 1    | u8     | TargetDetailLayer    | Default 0.                            |
| 47     | 1    | u8     | ObjectSetMask        | Default 0x0F.                         |
| 48     | 1    | u8     | CollisionGroup       | Default 3.                            |
| 49     | 1    | u8     | Flags                | Query flag bits. Default 0x49.        |
| 50     | 6    |        | Pad                  | 0.                                    |

A material hash and a surface hash are the MurmurHash2 of the lowercase name ([VSMI](sound.md#vsmi)). A material name is the path, for example `materials/tools/toolsclip.vmat`.

## PHRA

The Physics Debugger stream.

- Direction: S->C
- Version: 2

Message header (8 bytes):

| Offset | Size | Type   | Name       | Meaning                                                   |
| ------ | ---- | ------ | ---------- | --------------------------------------------------------- |
| 0      | 1    | u8     | Type       | Message type.                                             |
| 1      | 1    | u8     | Value      | 0 for all types except type 11. In type 11, a world mask. |
| 2      | 2    | u16 LE | Sequence   | Sequence number. See the next list.                       |
| 4      | 4    | u32 LE | BodyLength | Size of the body in bytes.                                |

- Types 0 and 1 are prolog messages. Their Sequence is always 218, and they do not advance the counter.
- All other types take Sequence from a 16-bit counter that increases by one for each message.

| Type | Trigger                                                                   | Body                                                                                                           |
| ---- | ------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| 0    | PHRB command 0.                                                           | Tick frequency (prolog form).                                                                                  |
| 1    | PHRB command 1. The server sends it one time and then clears the request. | Descriptor table of the type 3 fields (prolog form).                                                           |
| 2    | Each tick while state bit `0x4` is set, after the per-world messages.     | Empty. It marks the end of one tick.                                                                           |
| 3    | Each tick while state bit `0x8` is set, one for each world.               | World profile.                                                                                                 |
| 4    | PHRB command 4 with no data. Only the first world gets it.                | World debug draw mask.                                                                                         |
| 5    | PHRB command 5, and each time a world gets a new debug name.              | u64 object pointer, then a cstr debug name.                                                                    |
| 6    | PHRB command 6, after type 16, and when a world is created.               | World list.                                                                                                    |
| 7    | PHRB command 7.                                                           | Surface properties.                                                                                            |
| 11   | The console command `phys_debug_draw`. Value is the world mask.           | One draw filter ([PHRB](#phrb)).                                                                               |
| 14   | Each tick while state bit `0x4000` is set, one for each world.            | World counters.                                                                                                |
| 16   | Start of the stream.                                                      | u32 stream ID. Each new ID is the previous ID plus one, with bit `0x8000` set. PHRB command 12 can replace it. |
| 17   | End of the stream.                                                        | u32 stream ID, the same as in type 16.                                                                         |

Prolog body:

| Offset      | Size           | Type  | Name        | Meaning                                                                                                |
| ----------- | -------------- | ----- | ----------- | ------------------------------------------------------------------------------------------------------ |
| 0           | 4              | i32   | TableOffset | Offset of the descriptor table from the body start. 0 when there are no items.                         |
| 4           | 4              | u32   | TableCount  | Number of descriptor entries.                                                                          |
| 8           | var            |       | Data        | Type 0: u64 tick frequency of the CPU timer. Type 1: the item names as cstr values, with no alignment. |
| TableOffset | 8 x TableCount | entry | Table       | Descriptor entries.                                                                                    |

Descriptor entry (8 bytes):

| Offset | Size | Type | Name          | Meaning                                             |
| ------ | ---- | ---- | ------------- | --------------------------------------------------- |
| 0      | 1    | u8   | DataPointType | 1 for a u32 field. 2 for a u64 field.               |
| 1      | 1    | u8   | Pad           | 0.                                                  |
| 2      | 2    | u16  | Parent        | Index of the parent entry. 0xFFFF for a root entry. |
| 4      | 4    | i32  | NameOffset    | Offset of the name from the position of this field. |

The type 1 table has the entries "Id", "Create Flags", "Aggregate Count" and "Motion Controllers". "Motion Controllers" has "Aggregate Count" as its parent.

World profile (type 3, 20 bytes):

| Offset | Size | Type | Name              | Meaning                                |
| ------ | ---- | ---- | ----------------- | -------------------------------------- |
| 0      | 8    | u64  | Id                | World pointer.                         |
| 8      | 4    | u32  | CreateFlags       | Flags that the world was created with. |
| 12     | 4    | u32  | AggregateCount    | Number of aggregates.                  |
| 16     | 4    | u32  | MotionControllers | Number of motion controllers.          |

World debug draw mask (type 4, 16 bytes):

| Offset | Size | Type | Name          | Meaning                       |
| ------ | ---- | ---- | ------------- | ----------------------------- |
| 0      | 8    | u64  | World         | World pointer.                |
| 8      | 8    | u64  | DebugDrawMask | Debug draw bits of the world. |

World list (type 6). The entry repeats to the end of the body. There is no count.

| Offset | Size | Type | Name        | Meaning                                                              |
| ------ | ---- | ---- | ----------- | -------------------------------------------------------------------- |
| 0      | 8    | u64  | World       | World pointer.                                                       |
| 8      | 4    | u32  | CreateFlags | Flags that the world was created with.                               |
| 12     | 4    | u32  | WorldIndex  | Number that the world got from a global counter when it was created. |
| 16     | var  | cstr | DebugName   | Debug name of the world. A single NUL byte when the name is empty.   |

Surface properties (type 7):

| Size              | Type      | Name             | Meaning                                                    |
| ----------------- | --------- | ---------------- | ---------------------------------------------------------- |
| 4                 | u32       | UsedWordCount    | Number of words in UsedBits: (NameCount + 31) / 32.        |
| 4 x UsedWordCount | u32 array | UsedBits         | Bit i is set when a world uses surface property i.         |
| 4                 | u32       | NameCount        | Number of names.                                           |
| var               | cstr list | Names            | Surface property names, hidden properties included.        |
| 4                 | u32       | DescriptionCount | Number of descriptions.                                    |
| var               | cstr list | Descriptions     | Surface property descriptions, in the same order as Names. |

World counters (type 14, 28 bytes):

| Offset | Size | Type | Name       | Meaning                                        |
| ------ | ---- | ---- | ---------- | ---------------------------------------------- |
| 0      | 8    | u64  | World      | World pointer.                                 |
| 8      | 4    | u32  | Bodies     | Number of bodies.                              |
| 12     | 4    | u32  | Softbodies | Number of softbodies.                          |
| 16     | 4    | u32  | Joints     | Number of joints and null joints.              |
| 20     | 4    | u32  | Contacts   | Number of solver contacts and report contacts. |
| 24     | 4    | u32  | Reserved   | Always 0.                                      |
