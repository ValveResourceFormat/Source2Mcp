# Render system packets

## GPRT

The list of all GPU textures. No C->S packet requests GPRT.

- Direction: S->C
- Version: 1

| Offset | Size | Type   | Name    | Meaning                                               |
| ------ | ---- | ------ | ------- | ----------------------------------------------------- |
| 0      | 4    | u32 LE | Count   | Number of textures. A client MUST read Count entries. |
| 4      | var  | entry  | Entries | Texture entries.                                      |

Texture entry (18 bytes and a name):

| Offset | Size | Type   | Name        | Meaning                                                    |
| ------ | ---- | ------ | ----------- | ---------------------------------------------------------- |
| 0      | 2    | u16 BE | Width       | Width of the first descriptor (the disk specification).    |
| 2      | 2    | u16 BE | Height      | Height of the first descriptor.                            |
| 4      | 2    | u16 BE | Depth       | Depth of the first descriptor.                             |
| 6      | 2    | u16 BE | Width2      | Width of the second descriptor.                            |
| 8      | 2    | u16 BE | Height2     | Height of the second descriptor.                           |
| 10     | 2    | u16 BE | Depth2      | Depth of the second descriptor.                            |
| 12     | 2    | u16 BE | Flags       | Texture flags. See the next table.                         |
| 14     | 1    | u8     | Value1      | Not defined.                                               |
| 15     | 1    | u8     | Value2      | Not defined.                                               |
| 16     | 1    | u8     | Multisample | Multisample count, as in [SSDS](scene.md#ssds) subtype 5.  |
| 17     | 1    | u8     | Value3      | Not defined.                                               |
| 18     | var  | cstr   | Name        | Debug name, or the resource name when the texture has one. |

| Bit    | Meaning                                               |
| ------ | ----------------------------------------------------- |
| 0x0001 | 1D texture. The server never sets this bit.           |
| 0x0002 | 2D texture: neither a volume nor a cube map.          |
| 0x0004 | Volume texture.                                       |
| 0x0008 | Cube map.                                             |
| 0x0010 | Texture array.                                        |
| 0x0020 | Fallback: the texture has no GPU resource.            |
| 0x0040 | Render target.                                        |
| 0x0080 | Not defined.                                          |
| 0x0100 | No LOD: the texture skips the maximum-size reduction. |
| 0x0200 | Automatic mipmap generation.                          |
| 0x0400 | Pinned.                                               |
