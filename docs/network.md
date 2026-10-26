# Network packets

## DTIC

Controls the network encoding statistics (DTI).

- Direction: C->S
- Version: 3
- Module: networksystem

Payload: one KV3 object.

| Key     | Type | Meaning             |
| ------- | ---- | ------------------- |
| command | int  | See the next table. |

| Command | Effect                                                                  |
| ------- | ----------------------------------------------------------------------- |
| 0       | Turns DTI off.                                                          |
| 1       | Turns DTI on.                                                           |
| 2       | Resets the data.                                                        |
| 3       | Asks for an NDTI packet on the next frame.                              |
| 4       | Writes the data to a CSV file on the server.                            |
| 5       | Sets the external consumer flag. The server then sends no NDTI packets. |
| 6       | Clears the external consumer flag.                                      |

## NDTI

A network encoding statistics snapshot. The server sends it after DTIC command 3.

- Direction: S->C
- Version: 3
- Module: networksystem

Payload: one KV3 object.

| Key         | Type             | Meaning                                                                    |
| ----------- | ---------------- | -------------------------------------------------------------------------- |
| dti_enabled | bool             | DTI state.                                                                 |
| records     | array of objects | One object for each field with a decode count above 0. See the next table. |
| symbols     | array of string  | String table. Element i is the string of symbol index i.                   |

Each record object has these keys. All values are uint64.

| Key         | Meaning                                    |
| ----------- | ------------------------------------------ |
| classname   | Symbol index of the serializer class name. |
| field_short | Symbol index of the short field name.      |
| field_full  | Symbol index of the full field name.       |
| path        | Symbol index of the field path as text.    |
| field_notes | Symbol index of the field notes.           |
| decode      | Decode count. Always 1 or more.            |
| data_bits   | Total data bits.                           |
| index_bits  | Total index bits.                          |
| priority    | Field priority, a value from 0 to 255.     |

## NETC

Turns the net graph stream on or off. While the stream is on, the server sends NGRP one time and then NETG each frame.

- Direction: C->S
- Version: 3
- Module: networksystem

Payload: one KV3 object.

| Key    | Type | Meaning                                 |
| ------ | ---- | --------------------------------------- |
| enable | bool | True starts the stream. False stops it. |

## NETS

The network system is ready. The server sends it at the end of the network system initialization.

- Direction: S->C
- Version: 3
- Module: networksystem

The payload is empty.

## NGRP

Net graph group names and colors. The server sends it on the first net graph frame after NETC starts the stream.

- Direction: S->C
- Version: 3
- Module: networksystem

Payload: one KV3 object.

| Key        | Type             | Meaning                                      |
| ---------- | ---------------- | -------------------------------------------- |
| groupnames | array of objects | Each object has `name` (string) and `color`. |

## NETG

Net graph data for one frame. The server sends it each frame while the NETC stream is on.

- Direction: S->C
- Version: 3
- Module: networksystem

Payload: one KV3 object.

| Key                          | Type             | Meaning                                                                                                                                                                                                 |
| ---------------------------- | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| framerate                    | number           | Client frame rate.                                                                                                                                                                                      |
| client_interpolation_amount  | number           | Interpolation amount.                                                                                                                                                                                   |
| client_update_rate           | number           | Update rate.                                                                                                                                                                                            |
| avg_latency_peer             | number           | Average latency, peer.                                                                                                                                                                                  |
| avg_latency_local            | number           | Average latency, local.                                                                                                                                                                                 |
| avg_latency_net              | number           | Average latency, network.                                                                                                                                                                               |
| avg_packet_loss              | number           | Average packet loss.                                                                                                                                                                                    |
| incoming_sequence_start      | int              | First incoming sequence number in the graph.                                                                                                                                                            |
| outgoing_sequence_start      | int              | First outgoing sequence number in the graph.                                                                                                                                                            |
| incoming_sequence            | int              | Current incoming sequence number.                                                                                                                                                                       |
| outgoing_sequence            | int              | Current outgoing sequence number.                                                                                                                                                                       |
| incoming_data_bytespersecond | number           | Incoming data rate.                                                                                                                                                                                     |
| outgoing_data_bytespersecond | number           | Outgoing data rate.                                                                                                                                                                                     |
| avg_packet_in                | number           | Incoming packet rate.                                                                                                                                                                                   |
| avg_packet_out               | number           | Outgoing packet rate.                                                                                                                                                                                   |
| server_framerate             | number           | Server frame rate.                                                                                                                                                                                      |
| server_framerate_stddev      | number           | Standard deviation of the server frame rate.                                                                                                                                                            |
| unfiltered_server_framerate  | number           | Unfiltered server frame rate.                                                                                                                                                                           |
| graph                        | array of objects | One object for each sample. Keys: `unreliable_bits`, `reliable_bits`, `latency_seconds`, `packet_status`, `unreliable`, `reliable`, `prediction_errors`, `has_network_encoding_data`, `encoding_stats`. |
| cmd_info                     | array of objects | One object for each user command. Keys: `render_interpolation_fraction` (float), `bits` (int), `sent` (bool).                                                                                           |

## NETE

Network timing data for one recorded tick. The engine sends one NETE packet for each recorded tick, only while the net graph is on ([NETC](#netc)).

- Direction: S->C
- Version: 3
- Module: engine2

Payload: one KV3 object.

| Key                    | Type   | Meaning                                      |
| ---------------------- | ------ | -------------------------------------------- |
| server_cmd_queue       | uint   | Commands in the server queue.                |
| starved_ticks          | uint   | Ticks with no command.                       |
| discarded_ticks        | uint   | Discarded ticks.                             |
| server_tick            | int    | Server tick.                                 |
| tick_interval          | double | Tick interval of the game server in seconds. |
| server_cmd_recv_margin | double | Command receive margin. The default is -1.   |

## RCSR

Asks the game server for its RCON server details. The server copies the payload into the token field of the RCON server details message to the game server. The reply comes back as RCSI with the same token ([RCSI](#rcsi)).

- Direction: C->S
- Version: 1
- Module: networksystem

| Offset | Size | Type   | Name      | Meaning                      |
| ------ | ---- | ------ | --------- | ---------------------------- |
| 0      | 8    | f64 LE | FloatTime | Platform time of the client. |

## RCSI

RCON server details. The engine sends RCSI when the game client gets the RCON server details message from a game server.

- Direction: S->C
- Version: 1
- Module: engine2

| Offset | Size | Type     | Name    | Meaning                                                                            |
| ------ | ---- | -------- | ------- | ---------------------------------------------------------------------------------- |
| 0      | 8    | bytes[8] | Token   | Copy of the RCSR payload.                                                          |
| 8      | var  | bytes    | Details | The details string. No NUL and no length field. It ends at the end of the payload. |

The game server sets details to the string cvar `vconsole_rcon_server_details` and sends no reply when it is empty. If a client sends an RCSR payload with another length than 8 bytes, the RCSI token has that length.

A client MUST reject an RCSI packet with a payload of 8 bytes or fewer.
