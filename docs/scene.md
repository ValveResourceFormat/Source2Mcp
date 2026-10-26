# Scene system packets

## SSDC

A scene system debugger command.

- Direction: C->S
- Version: 4

| Offset | Size | Type   | Name    | Meaning             |
| ------ | ---- | ------ | ------- | ------------------- |
| 0      | 4    | u32 BE | Command | See the next table. |

| Command | Effect                                                                                         |
| ------- | ---------------------------------------------------------------------------------------------- |
| 0       | The server sends the scene system statistics (SSDF kind 0).                                    |
| 1       | The server starts a timer, and later sends SSDF kind 1.                                        |
| 2       | The server sends the list of views as SSDS subtype 0 in the next scene system debugger update. |
| other   | Fatal error. The game process stops ([Fatal error packets](errors.md#fatal-error-packets)).    |

## SSDS

View, layer, render target and texture data for the scene system debugger. The first u32 BE selects the subtype.

- Direction: S->C
- Version: 4

| Subtype | Data after the subtype                                                                                                                                                                                                                                                                                      |
| ------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0       | KV3 with key `m_Views`.                                                                                                                                                                                                                                                                                     |
| 2, 4    | KV3 with keys `m_viewId`, `m_ViewName`, `m_nLayerId`, `m_LayerName`, `m_displayText`.                                                                                                                                                                                                                       |
| 3       | KV3 with keys `m_viewId`, `m_ViewName`, `m_Targets`. Each target has `m_Name`, `m_TextureId`, `m_nWidth`, `m_nHeight`, `m_nRequestedWidth`, `m_nRequestedHeight`, `m_nNumMipLevels`, `m_nDepth`, `m_nMultisampleNumSamples`, `m_nFormat`. A second KV3 object follows with `m_nViewId` and `m_nFrameCount`. |
| 5       | One chunk of texture pixels. See the next table.                                                                                                                                                                                                                                                            |

A client MUST skip other subtypes.

Subtype 5. The server splits a texture into chunks that fit the packet size limit.

| Offset | Size | Type     | Name          | Meaning                                                              |
| ------ | ---- | -------- | ------------- | -------------------------------------------------------------------- |
| 0      | 4    | u32 BE   | Subtype       | 5.                                                                   |
| 4      | 8    | u64 BE   | Value1        | Identifier of the view. It matches `m_viewId` in the other subtypes. |
| 12     | 8    | u64 BE   | FrameCount    | Frame count.                                                         |
| 20     | 8    | u64 BE   | Value2        | Identifier of the view render data.                                  |
| 28     | 8    | u64 BE   | TextureHandle | Texture handle.                                                      |
| 36     | 1    | u8       | Index         | Render target set, 0 or 1.                                           |
| 37     | 1    | u8       | Slot          | Render target slot. 0xFF is the depth target.                        |
| 38     | 1    | u8       | Zero          | Always 0.                                                            |
| 39     | 1    | u8       | Zero2         | Always 0.                                                            |
| 40     | 2    | u16 BE   | Width         | Width in pixels.                                                     |
| 42     | 2    | u16 BE   | Height        | Height in pixels.                                                    |
| 44     | 2    | u16 BE   | ChunkCount    | Number of chunks.                                                    |
| 46     | 2    | u16 BE   | ChunkIndex    | Index of this chunk.                                                 |
| 48     | 2    | u16 BE   | FirstRow      | First pixel row in this chunk.                                       |
| 50     | 2    | u16 BE   | RowCount      | Number of pixel rows in this chunk.                                  |
| 52     | 4    | u32 BE   | Format        | Texture format.                                                      |
| 56     | 4    | u32 BE   | ByteCount     | Number of pixel bytes.                                               |
| 60     | 4    | u32 LE   | Multisample   | Multisample count: 1, 2, 4, 6, 8 or 16.                              |
| 64     | 64   | char[64] | Name          | Texture name.                                                        |
| 128    | 4    | bytes[4] | Padding       | Always 0. A client MUST skip it.                                     |
| 132    | var  | bytes    | Pixels        | Pixel data. The packet payload size is ByteCount plus 132.           |

## SSDF

Scene system statistics and timer results. The server sends it after SSDC command 0 or 1. The payload is big-endian. The first u32 BE selects the kind.

- Direction: S->C
- Version: 4

| Kind | Data after the kind                          |
| ---- | -------------------------------------------- |
| 0    | Scene system statistics. See the next table. |
| 1    | Text "Timer took %.4f".                      |

Kind 0 data:

| Field                       | Type                          | Meaning                                          |
| --------------------------- | ----------------------------- | ------------------------------------------------ |
| view count                  | u16 BE                        | Number of views.                                 |
| for each view: name         | char[260]                     | View name.                                       |
| for each view: item count   | u16 BE                        | Number of item records.                          |
| for each view: items        | item records                  | Variable size. See the next table.               |
| for each view: layer count  | u16 BE                        | Number of layer records.                         |
| for each view: layer record | char[260], u16 BE x 5, u8, u8 | Layer name and values. The record has 272 bytes. |

Item record:

| Field    | Type       | Meaning                                                               |
| -------- | ---------- | --------------------------------------------------------------------- |
| Name1    | char[260]  | First name.                                                           |
| Name2    | char[260]  | Second name.                                                          |
| Flag     | u8         | Not defined.                                                          |
| Mode     | u8         | If 0, the entries below follow. If not 0, the record ends after Mode. |
| CountA   | u16 BE     | Number of A entries. Only when Mode is 0.                             |
| EntriesA | 7 x CountA | A entries: u32 BE, u8, u8, u8. Not defined. Only when Mode is 0.      |
| CountB   | u16 BE     | Number of B entries. Only when Mode is 0.                             |
| EntriesB | 7 x CountB | B entries: u32 BE, u8, u8, u8. Not defined. Only when Mode is 0.      |
