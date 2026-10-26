# Animation packets

## ANIM (C->S)

Asks for animation samples. The server does not read the payload. A client SHOULD send an empty payload.

- Direction: C->S
- Version: 1

## ANIM (S->C)

Animation samples for each entity. The server sends ANIM each animation frame while a request is active. When no samples arrive for 1 second, the server sends one element with entity index -1 ("no samples").

- Direction: S->C
- Version: 1

Payload: one KV3 object.

| Key              | Type             | Meaning                                                                                                                                                |
| ---------------- | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| m_processingType | int              | Value of the enum `AnimationProcessingType_t`: 0 server simulation, 1 client simulation, 2 client prediction, 3 client interpolation, 4 client render. |
| m_elems          | array of objects | One object for each entity. Keys: `m_nEntityIndex`, `m_modelName`, `m_poseParams`, `m_decodeOps`, `m_internalOps`, `m_decodedAnims`.                   |

## ANID

The end of an ANIM batch.

- Direction: S->C
- Version: 1

The payload is empty.

## ANIE

Selects the entity to record. This entity is the debug entity of [ANTF](#antf).

- Direction: C->S
- Version: 1

| Offset | Size | Type   | Name        | Meaning       |
| ------ | ---- | ------ | ----------- | ------------- |
| 0      | 4    | u32 LE | EntityIndex | Entity index. |

## ANMS

A bone snapshot of one entity.

- Direction: S->C
- Version: 1

The payload has 1 byte after the KV3 data. A client MUST ignore it.

Payload: one KV3 object.

| Key                  | Type      | Meaning                               |
| -------------------- | --------- | ------------------------------------- |
| m_nEntIndex          | int       | Entity index.                         |
| m_modelName          | string    | Model name.                           |
| m_flRealTime         | double    | Time.                                 |
| m_rootToWorld        | transform | Root transform.                       |
| m_bBonesInWorldSpace | bool      | True if the bones are in world space. |
| m_boneSetupMask      | int       | Bone setup mask.                      |
| m_boneTransforms     | array     | Bone transforms.                      |
| m_flexControllers    | array     | Flex controller values.               |
| m_SnapshotType       | int       | Snapshot type.                        |
| m_bHasDecodeDump     | bool      | True when `m_DecodeDump` is present.  |
| m_DecodeDump         | object    | Decode dump.                          |

## ANTF

Time marks for the animation debugger. The game sends ANTF only while a debug entity is set ([ANIE](#anie)).

- Direction: S->C
- Version: 1

| Offset | Size | Type   | Name       | Meaning                                              |
| ------ | ---- | ------ | ---------- | ---------------------------------------------------- |
| 0      | 8    | f64 LE | ClockStart | Start time of the platform clock.                    |
| 8      | 4    | f32 LE | FloatTime  | Current platform time in seconds.                    |
| 12     | 4    | i32 LE | SourceID   | Code point that sent the packet. See the next table. |

| SourceID | Sender                 |
| -------- | ---------------------- |
| 0        | Server game frame.     |
| 1        | Client frame.          |
| 2        | Client prediction.     |
| 3, 4, 5  | Client network update. |
