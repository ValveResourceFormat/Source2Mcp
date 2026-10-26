# Transport

How a client connects to the server, the packet format, and the connection lifecycle.

## Connection

The protocol uses one TCP connection over IPv4. The server binds to address 0.0.0.0 on port 29000.

The server listens immediately after it loads the VConsole2 module, before the first map loads.

## Command-line parameters

The engine loads the VConsole2 module only when all these conditions are true:

1. The command line has `-tools` or `-insecure`.
2. The application has the `isgame` value set, or the command line has `-vconsole` or `-vconport`.
3. The command line has no `-nv` and no `-novconsole`.

A game sets `isgame` to 1. Thus, in a game, `-insecure` alone starts the server.

| Parameter                  | Default | Effect                                                                                                              |
| -------------------------- | ------- | ------------------------------------------------------------------------------------------------------------------- |
| `-insecure`                | -       | Enables the server.                                                                                                 |
| `-tools`                   | -       | Enables the server and tools mode ([Hidden commands](console.md#hidden-commands)).                                  |
| `-vconport <n>`            | 29000   | TCP port. Satisfies condition 2.                                                                                    |
| `-vconmax <n>`             | 1       | Number of client slots. The server clamps the value to 1 to 20.                                                     |
| `-vconbuffer <KB>`         | 8192    | Size of the PRNT history in KB.                                                                                     |
| `-wv <s>`, `-vconsole <s>` | -       | Wait for a client during startup ([Wait for a client](#wait-for-a-client)). `-vconsole` also satisfies condition 2. |
| `-nv`, `-novconsole`       | -       | Disable the server.                                                                                                 |

## Client slots

The server has `-vconmax` client slots. It accepts a new connection only when a slot is free. When all slots are full, the connection waits in the listen backlog (16 entries) and gets no data until a slot is free.

A client MUST NOT expect data immediately after the TCP connection opens. The console command `vcon_clients` lists the connected clients.

A connection that the server accepts right after another client closes can get duplicate PRNT and PONG packets and can lose data. During that time, `vcon_clients` can list one more client than the slot count.

A client SHOULD send PING after the connect sequence and use a timeout of several seconds ([PING](console.md#ping)). If no PONG arrives, the client SHOULD close the connection and connect again after a short wait. A client SHOULD accept duplicate PRNT and PONG packets.

## Wait for a client

With `-wv` or `-vconsole`, the server waits for a client during startup. The value is a timeout in seconds. `-vconsole` overrides `-wv`. The wait ends when the server accepts a TCP connection.

| Value                                     | Effect                                 |
| ----------------------------------------- | -------------------------------------- |
| none (parameter last on the command line) | -1 with `-consoleapp`, else 0.         |
| not a number                              | 0.                                     |
| 0                                         | No wait.                               |
| -1                                        | Wait with no timeout.                  |
| -2 or less                                | No wait.                               |
| more than 0                               | Wait for up to that number of seconds. |

## Authentication

There is no handshake and no authentication. The server sends the first packets.

## Port in use

If the server cannot bind the port, the game shows an error and exits. Use `-vconport <n>`. Each game process needs its own port.

## Packet format

### Header

| Offset | Size | Type    | Name    | Meaning                                                               |
| ------ | ---- | ------- | ------- | --------------------------------------------------------------------- |
| 0      | 4    | char[4] | Tag     | 4 ASCII characters in reading order. `PRNT` is `50 52 4E 54`. No NUL. |
| 4      | 2    | u16 BE  | Version | Version of this tag ([Version](#version)).                            |
| 6      | 4    | u32 BE  | Length  | Total packet length in bytes, header included.                        |
| 10     | 2    | u16 BE  | Handle  | Always 0.                                                             |

A sender MUST write 0 in the Handle field. A receiver MUST ignore the Handle field.

### Version

The default version is 212 (`00 D4`). [Tags](readme.md#tags) gives the version of each tag. When a module sends a packet with version 0, the server writes 212 in the header.

Each handler registers a tag and a version, and checks the version itself. When a handler rejects a packet, the server drops it, sends no error packet and logs these lines on the channel `VConComm` with severity 3:

- "Message Version Mismatch: '%s', Expected %d, Got %d", when the version differs from the registered version.
- "VConsole Rejecting Message: '%s', No Message Handler Claimed Ownership.", each time that a handler returns false.

The server logs nothing for an accepted packet.

Some handlers register version 0 and check the version inside the handler: ACTV (212), BUGI, BUGS, BUGO and BUGC (3). For these tags, the mismatch log line shows "Expected 0".

A client MUST send each tag with the version in [Tags](readme.md#tags). For older servers, [Protocol versions](versions.md) gives the versions. A client SHOULD accept S->C packets with an unexpected version and skip them by their Length.

### Length

| Limit               | Value                                                                                                                                                                      |
| ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Smallest Length     | 12.                                                                                                                                                                        |
| Largest S->C packet | 8 MiB (`0x800000`), header included. The server drops a larger packet, except some PRNT packets to a client at 127.0.0.1 ([Oversize packets](errors.md#oversize-packets)). |
| Largest C->S packet | 8 MiB, header included. The server processes a packet of exactly 8 MiB.                                                                                                    |

The server receive buffer starts at 1 MiB. It grows to 8 MiB while a packet of 1 MiB or more is pending, and returns to 1 MiB when it is empty.

A client MUST accept packets up to 8 MiB. For a C->S Length less than 12, see [Length less than 12](errors.md#length-less-than-12).

### Stream parsing

TCP does not keep packet borders. One read can hold part of a packet, or more than one packet. A receiver MUST parse the stream in this sequence:

1. Add the received bytes to a buffer.
2. If the buffer has fewer than 12 bytes, read more bytes.
3. Read Length from offset 6.
4. If Length is less than 12, close the connection.
5. If the buffer has fewer than Length bytes, read more bytes.
6. Process the first Length bytes as one packet, and remove them from the buffer.
7. Go to step 2.

A receiver MUST skip a packet with an unknown tag by its Length.

### Queues

The server processes a C->S CHAN packet on the network thread immediately. The server puts all other C->S packets in a queue. The main thread processes the queue once per frame. Thus, the game must run frames to process CMND.

Each client has an outgoing queue on the server. When a queue holds more than 1024 packets, the thread that adds a packet waits. It waits until the queue holds 512 packets or fewer, for at most 50 x 5 ms. The server drops no packet and keeps the connection open. A client SHOULD read data continuously.

The server has no idle timeout.

## Connection lifecycle

### Connect sequence

When a client connects, the server sends these packets in this sequence:

| Step | Packet | Recipients  | Content                                                                                                |
| ---- | ------ | ----------- | ------------------------------------------------------------------------------------------------------ |
| 1    | AINF   | this client | Application information.                                                                               |
| 2    | CHAN   | this client | All log channels.                                                                                      |
| 3    | PRNT   | this client | "Connection in progress..."                                                                            |
| 4    | ADON   | this client | Addon list.                                                                                            |
| 5    | CVRB   | all clients | Full cvar and command list.                                                                            |
| 6    | PRNT   | this client | History start banner, 3 lines ([History replay](#history-replay)). Only when the history is not empty. |
| 7    | PRNT   | this client | "Prior Messages Discarded at %d bytes", only when the history lost old lines.                          |
| 8    | PRNT   | this client | All PRNT packets in the history.                                                                       |
| 9    | PRNT   | this client | History end banner, 3 lines. Only when the history is not empty.                                       |
| 10   | PRNT   | all clients | "VConsole client connection from a.b.c.d"                                                              |

Servers that track modules also send MODM, SVME and SVMI after ADON ([MODM](console.md#modm)). A client that connects at process start can get two AINF packets ([AINF](console.md#ainf)). A client MUST accept MODM, SVME, SVMI, AINF, ADON and single-entry CVRB packets at any position in the stream.

A client MUST NOT depend on the sequence above. A client SHOULD start to send packets after it gets AINF.

### History replay

The server keeps a copy of each PRNT packet in a history buffer of `-vconbuffer` KB. On connect, it sends the history between two banners. Each banner is 3 PRNT packets on the channel `VConComm`:

```
==========================================================
=============== VConsole Buffered Messages ===============
==========================================================
```

```
==============================================================
=============== End VConsole Buffered Messages ===============
==============================================================
```

Each line ends with `\n`. History packets keep their original fields, Time included. The history includes the connection lines of earlier clients. The console command `vcon_clear_buffered_log` clears the history. When the history is empty, the server sends no banners. A client that shows only new output SHOULD discard PRNT packets until the third end banner line. If no start banner came, it SHOULD discard them until the connection line.

### Reconnect

A client MAY disconnect and connect again at any time. The server sends the full connect sequence and history again.

### Disconnect

Either side closes the TCP connection to end the session. The server frees the slot. There is no disconnect packet.

When the game main loop ends, the server sends CMND `vc_exit engine` to all clients (`vc_exit tools` in tools mode). A client MAY close the connection when it gets this packet.

### Not sent

The server does not send:

- A keepalive packet.
- A PRNT echo of a received CMND, except on a dedicated server ([CMND processing](console.md#cmnd-processing)).

## Security considerations

- The protocol has no authentication and no encryption. Any host that can reach the port can connect.
- Block the port from untrusted networks.
- CMND text runs as console input with the full rights of the console. This includes commands that write files, run configs and connect to servers.
- Each client gets all log output, the output of commands from other clients, and the IP address of each new client.
- AINF holds the full command line of the game. The command line can contain account tokens or passwords.
- Some malformed packets stop the game process ([Fatal error packets](errors.md#fatal-error-packets)).
- A C->S packet with Length below 12 or above 8 MiB can stop all later processing for that client. See [Length less than 12](errors.md#length-less-than-12) and [Oversize packets](errors.md#oversize-packets).
