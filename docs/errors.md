# Errors and limits

## Dropped packets

The server drops a packet with a version it does not accept or a tag with no handler, and logs the lines in [Version](transport.md#version). A client MUST NOT close the connection because of an unknown tag.

## Length less than 12

A C->S packet with Length 0 makes the server loop on that connection. Each pass queues an empty message that logs "VConsole Rejecting Message", and the server memory grows.

The server treats a C->S packet with Length 1 to 11 as a message of Length bytes with tag 0 and version 0. It logs "VConsole Rejecting Message". It then reads the next Length at offset 6 from (start of that packet + Length):

- When that Length is more than 8 MiB, the server stalls and processes no more packets from that client.
- When it is 12 to 8 MiB, the server waits for that number of bytes and dispatches them as one message. Then it reads correct packets again.
- When it is 0 or 1 to 11, the rules above apply again.

For example, after a full 12-byte header, when the next packet is a 20-byte PING, the server reads these Lengths:

| Bad Length | Next Length     | Result     |
| ---------- | --------------- | ---------- |
| 1          | 256             | Continues. |
| 2          | 131072          | Continues. |
| 3          | More than 8 MiB | Stalls.    |
| 4          | 20553           | Continues. |
| 5          | 5261646         | Continues. |
| 6 to 9     | More than 8 MiB | Stalls.    |
| 10         | 65536           | Continues. |
| 11         | More than 8 MiB | Stalls.    |

A stalled connection does not affect other connected clients. It keeps its slot until the client closes it. A client that closes a stalled connection SHOULD wait a short time before it connects again ([Client slots](transport.md#client-slots)).

A client MUST NOT send a packet with Length less than 12. A client that sent such a packet MUST close the connection and connect again.

## Oversize packets

C->S: a packet larger than 8 MiB never completes. The server then processes no more packets from that client. A client MUST NOT send a packet larger than 8 MiB.

S->C: the server drops a packet larger than 8 MiB and logs "VConComm Discarding Oversized Outgoing Message". Some PRNT packets can exceed 8 MiB to a client at 127.0.0.1.

## Fatal error packets

These C->S packets stop the game process:

- An [SSDC](scene.md#ssdc) packet with a Command value outside 0 to 2 ("Unrecognized command %d in scene system debugger command").
- A [PHRB](physics.md#phrb) command 8 or 10 whose draw filter size does not match its counts ("Buffer overflow").

A client MUST NOT send a subsystem C->S packet unless it writes the payload exactly as its page specifies.

## Inaccurate lengths and garbage bytes

The Length of BLCK, GRPS and TRCK counts one entry more than Count. When SVDM or SMVM has Count 0, Length still counts one entry. The extra entry holds garbage bytes.

The soundsystem senders of SVSM, SVDM, SMVM, SMXI, BLCK, GRPS and TRCK write Length from a u16 size. A payload of 65536 bytes or more loses its top bits. The server sends only the first (size mod 65536) bytes, and Count still gives the full number of entries.

A receiver MUST read at most the smaller of Count and the number of entries that fit in the payload. A receiver MUST use Length only to find the next packet.
