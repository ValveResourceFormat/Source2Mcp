# VConsole2 Protocol

Source 2 VConsole2 TCP protocol, protocol version 212.

## Overview

VConsole2 is the remote console of the Source 2 engine. The game process is the server. A tool that connects to it is the client.

The server sends log lines, the cvar and command list, and application data. The client sends console commands and tool requests. All data goes over one TCP connection as a sequence of packets. Each packet has a 12-byte header and a payload.

The protocol has no authentication. Console commands have no request IDs and no replies.

## Conventions

### Terms

| Term    | Meaning                                                                  |
| ------- | ------------------------------------------------------------------------ |
| server  | The game process. It loads the VConsole2 module and listens for clients. |
| client  | A program that connects to the server.                                   |
| packet  | One framed unit: a 12-byte header and a payload.                         |
| tag     | The 4 ASCII characters at the start of a packet, for example `PRNT`.     |
| version | The u16 after the tag. Each tag has its own version.                     |
| payload | The bytes after the 12-byte header.                                      |
| handler | The server function that a module registers for one tag.                 |
| S->C    | Direction from the server to the client.                                 |
| C->S    | Direction from the client to the server.                                 |

### Byte order

The packet header is big-endian (BE).

Payload byte order differs between packets and between fields. Each field table gives the byte order of each multi-byte field. A type without a suffix has one byte, has no byte order, or is on a page that states one byte order for all its fields.

These fields are little-endian (LE) in otherwise big-endian packets:

| Packet         | Field       |
| -------------- | ----------- |
| SSDS subtype 5 | Multisample |
| SDCA entry A   | LoadTime    |
| SDCA entry B   | Time        |

These packets have only little-endian fields: ACTV (C->S), BUGI (S->C), BUGS, FRMS, ANTF, RSPF, RCSR, ANIE, PHRA, PHRB, and the soundsystem packets except SDCA and SDCU. GPRT mixes both byte orders. KV3 data is little-endian inside its own container.

### Types

| Type              | Meaning                                                                                       |
| ----------------- | --------------------------------------------------------------------------------------------- |
| u8, u16, u32, u64 | Unsigned integer of 1, 2, 4 or 8 bytes.                                                       |
| i32               | Signed two's complement integer of 4 bytes.                                                   |
| f32, f64          | IEEE 754 binary32 or binary64 number.                                                         |
| char[n]           | Text field of exactly n bytes. See the rules below.                                           |
| cstr              | Text of variable length, followed by one NUL byte.                                            |
| bytes[n], bytes   | Raw bytes. The Size column gives the size. A size of "var" means "to the end of the payload". |
| cstr list         | cstr values one after another. Another field or the payload end gives the count.              |
| T x n, T[n]       | n values of type T one after another.                                                         |
| kv3               | One binary KeyValues3 object ([KV3 payloads](#kv3-payloads)).                                 |

Text is UTF-8. A receiver MUST read a char[n] field only up to its first NUL byte. A receiver MUST NOT expect the bytes after that NUL to be zero.

A sender MUST write a char[n] field with a NUL byte at or before byte n - 1. A sender SHOULD fill the bytes after the NUL with zeros.

### KV3 payloads

Many packets carry one binary KeyValues3 (KV3) object as the whole payload. The server writes S->C KV3 with no compression.

The handlers of DTIC, ENTC, PRDC, NETC and RCMD accept text or binary KV3. A handler that cannot parse the payload ignores it and logs nothing. A client SHOULD send binary KV3.

The KV3 key tables give the key, the KV3 type and the meaning. The types int, int32, uint, uint32, int64 and uint64 are KV3 integers. The types float, double and number are KV3 numbers. The other types are bool, string, array and object.

A client MUST read KV3 values by key and MUST NOT depend on key order. A client SHOULD accept unknown keys.

## Pages

- [transport](transport.md): Connection, packet format, connection lifecycle, security.
- [console](console.md): Console packets: application info, channels, log output, cvars, commands and command output.
- [engine](engine.md): Frame stats, bug reports, VProf, entity report and prediction.
- [network](network.md): Network system packets.
- [animation](animation.md): Animation system packets.
- [resources](resources.md): Resource system profiling.
- [sound](sound.md): Sound system packets.
- [scene](scene.md): Scene system packets.
- [physics](physics.md): Physics debugger packets.
- [particles](particles.md): Particle system packets.
- [render](render.md): Render system packets.
- [errors](errors.md): Error handling and limits.
- [versions](versions.md): Older protocol versions.

## Client minimum

A console client needs AINF, PRNT, CVRB and CMND. It MAY also use CHAN, AUTO, PING and PONG. It MAY skip all other S->C packets, including those of the subsystem pages.

## Tags

Direction "both" means that the tag has a C->S form and an S->C form. Tags marked "legacy" occur only in older protocol forms.

| Tag  | Version    | Direction | Module                                     | Page                                                                                     |
| ---- | ---------- | --------- | ------------------------------------------ | ---------------------------------------------------------------------------------------- |
| ACTV | 212        | both      | vconcomm (S->C), engine2 (C->S)            | [ACTV and ANOF (S->C)](console.md#actv-and-anof-s-c), [ACTV (C->S)](console.md#actv-c-s) |
| ADON | 212        | S->C      | vconcomm                                   | [ADON](console.md#adon)                                                                  |
| AINF | 212        | S->C      | vconcomm                                   | [AINF](console.md#ainf)                                                                  |
| ANID | 1          | S->C      | animationsystem                            | [ANID](animation.md#anid)                                                                |
| ANIE | 1          | C->S      | animationsystem                            | [ANIE](animation.md#anie)                                                                |
| ANIM | 1          | both      | animationsystem                            | [ANIM (C->S)](animation.md#anim-c-s), [ANIM (S->C)](animation.md#anim-s-c)               |
| ANMS | 1          | S->C      | animationsystem, scenesystem               | [ANMS](animation.md#anms)                                                                |
| ANOF | 212        | S->C      | vconcomm                                   | [ACTV and ANOF (S->C)](console.md#actv-and-anof-s-c)                                     |
| ANTF | 1          | S->C      | server, client                             | [ANTF](animation.md#antf)                                                                |
| AUTO | 2          | both      | engine2                                    | [AUTO (C->S)](console.md#auto-c-s), [AUTO (S->C)](console.md#auto-s-c)                   |
| BLCK | 3          | S->C      | soundsystem                                | [BLCK](sound.md#blck)                                                                    |
| BUGC | 3          | C->S      | engine2                                    | [BUGO and BUGC](engine.md#bugo-and-bugc)                                                 |
| BUGI | 3          | both      | engine2                                    | [BUGI](engine.md#bugi)                                                                   |
| BUGO | 3          | C->S      | engine2                                    | [BUGO and BUGC](engine.md#bugo-and-bugc)                                                 |
| BUGS | 3          | both      | engine2                                    | [BUGS](engine.md#bugs)                                                                   |
| CFGV | 207 to 212 | S->C      | vconcomm (legacy)                          | [Protocol versions](versions.md)                                                         |
| CHAN | 212        | both      | vconcomm                                   | [CHAN (S->C)](console.md#chan-s-c), [CHAN (C->S)](console.md#chan-c-s)                   |
| CMND | 212        | both      | engine2 (C->S), vconcomm and client (S->C) | [CMND (C->S)](console.md#cmnd-c-s), [CMND (S->C)](console.md#cmnd-s-c)                   |
| CVAR | 207 to 212 | S->C      | vconcomm (legacy)                          | [Protocol versions](versions.md)                                                         |
| CVRB | 2          | S->C      | vconcomm                                   | [CVRB](console.md#cvrb), [Protocol versions](versions.md) (version 1)                    |
| DTIC | 3          | C->S      | networksystem                              | [DTIC](network.md#dtic)                                                                  |
| ECHG | 5          | S->C      | engine2                                    | [ECHG](engine.md#echg)                                                                   |
| EFUL | 5          | S->C      | engine2                                    | [EFUL](engine.md#eful)                                                                   |
| ENTC | 5          | C->S      | engine2                                    | [ENTC](engine.md#entc)                                                                   |
| ENTR | 5          | S->C      | engine2                                    | [ENTR](engine.md#entr)                                                                   |
| EPLY | 5          | S->C      | engine2                                    | [EPLY](engine.md#eply)                                                                   |
| EXIT | 212        | S->C      | none (client only)                         | [EXIT](console.md#exit)                                                                  |
| FRMS | 2          | S->C      | engine2                                    | [FRMS](engine.md#frms)                                                                   |
| GPRT | 1          | S->C      | rendersystem                               | [GPRT](render.md#gprt)                                                                   |
| GRPS | 3          | S->C      | soundsystem                                | [GRPS](sound.md#grps)                                                                    |
| GRPT | 1          | S->C      | particles                                  | [GRPT](particles.md#grpt)                                                                |
| MODM | 212        | S->C      | vconcomm                                   | [MODM](console.md#modm)                                                                  |
| NDTI | 3          | S->C      | networksystem                              | [NDTI](network.md#ndti)                                                                  |
| NETC | 3          | C->S      | networksystem                              | [NETC](network.md#netc)                                                                  |
| NETE | 3          | S->C      | engine2                                    | [NETE](network.md#nete)                                                                  |
| NETG | 3          | S->C      | networksystem                              | [NETG](network.md#netg)                                                                  |
| NETS | 3          | S->C      | networksystem                              | [NETS](network.md#nets)                                                                  |
| NGRP | 3          | S->C      | networksystem                              | [NGRP](network.md#ngrp)                                                                  |
| PCAT | 3          | S->C      | engine2                                    | [PCAT](engine.md#pcat)                                                                   |
| PCFG | 3          | S->C      | engine2                                    | [PCFG](engine.md#pcfg)                                                                   |
| PCPU | 3          | S->C      | engine2                                    | [PCPU](engine.md#pcpu)                                                                   |
| PHRA | 2          | S->C      | vphysics2                                  | [PHRA](physics.md#phra)                                                                  |
| PHRB | 2          | C->S      | vphysics2                                  | [PHRB](physics.md#phrb)                                                                  |
| PING | 1          | C->S      | vconcomm                                   | [PING](console.md#ping)                                                                  |
| PONG | 1          | S->C      | vconcomm                                   | [PONG](console.md#pong)                                                                  |
| PPCL | 207 to 212 | both      | vconcomm (legacy)                          | [Protocol versions](versions.md)                                                         |
| PPCR | 207 to 212 | both      | vconcomm (legacy)                          | [Protocol versions](versions.md)                                                         |
| PPOA | 207 to 212 | both      | vconcomm (legacy)                          | [Protocol versions](versions.md)                                                         |
| PPOR | 207 to 212 | both      | vconcomm (legacy)                          | [Protocol versions](versions.md)                                                         |
| PRDC | 1          | C->S      | client                                     | [PRDC](engine.md#prdc)                                                                   |
| PRED | 1          | S->C      | client                                     | [PRED](engine.md#pred)                                                                   |
| PRHE | 1          | S->C      | client                                     | [PRHE](engine.md#prhe)                                                                   |
| PRNT | 212        | S->C      | vconcomm                                   | [PRNT](console.md#prnt)                                                                  |
| PTAT | 3          | S->C      | engine2                                    | [PTAT](engine.md#ptat)                                                                   |
| PTEX | 3          | S->C      | engine2                                    | [PTEX](engine.md#ptex)                                                                   |
| RCMD | 7          | C->S      | resourcesystem                             | [RCMD](resources.md#rcmd)                                                                |
| RCSI | 1          | S->C      | engine2                                    | [RCSI](network.md#rcsi)                                                                  |
| RCSR | 1          | C->S      | networksystem                              | [RCSR](network.md#rcsr)                                                                  |
| RSPF | 7          | S->C      | resourcesystem                             | [RSPF](resources.md#rspf)                                                                |
| SDCA | 3          | S->C      | soundsystem                                | [SDCA](sound.md#sdca)                                                                    |
| SDCU | 3          | S->C      | soundsystem                                | [SDCU](sound.md#sdcu)                                                                    |
| SEVT | 3          | S->C      | soundsystem                                | [SEVT](sound.md#sevt)                                                                    |
| SMVM | 3          | S->C      | soundsystem                                | [SVSM, SVDM and SMVM](sound.md#svsm-svdm-and-smvm)                                       |
| SMXI | 3          | S->C      | soundsystem                                | [SMXI](sound.md#smxi)                                                                    |
| SMXR | 3          | S->C      | soundsystem                                | [SMXR](sound.md#smxr)                                                                    |
| SNDS | 3          | S->C      | soundsystem                                | [SNDS](sound.md#snds)                                                                    |
| SSDC | 4          | C->S      | scenesystem                                | [SSDC](scene.md#ssdc)                                                                    |
| SSDF | 4          | S->C      | scenesystem                                | [SSDF](scene.md#ssdf)                                                                    |
| SSDS | 4          | S->C      | scenesystem                                | [SSDS](scene.md#ssds)                                                                    |
| SVDM | 3          | S->C      | soundsystem                                | [SVSM, SVDM and SMVM](sound.md#svsm-svdm-and-smvm)                                       |
| SVME | 212        | S->C      | vconcomm                                   | [SVME](console.md#svme)                                                                  |
| SVMI | 212        | S->C      | vconcomm                                   | [SVMI](console.md#svmi)                                                                  |
| SVSM | 3          | S->C      | soundsystem                                | [SVSM, SVDM and SMVM](sound.md#svsm-svdm-and-smvm)                                       |
| TRCK | 3          | S->C      | soundsystem                                | [TRCK](sound.md#trck)                                                                    |
| USRN | 212        | C->S      | vconcomm                                   | [USRN](console.md#usrn)                                                                  |
| VFCS | 212        | C->S      | vconcomm                                   | [VFCS](console.md#vfcs)                                                                  |
| VMPC | 3          | C->S      | engine2 (always rejected)                  | [VMPN and VMPC](engine.md#vmpn-and-vmpc)                                                 |
| VMPN | 3          | C->S      | engine2 (always rejected)                  | [VMPN and VMPC](engine.md#vmpn-and-vmpc)                                                 |
| VPRF | 3          | C->S      | engine2                                    | [VPRF](engine.md#vprf)                                                                   |
| VSBP | 3          | C->S      | soundsystem                                | [VSBP](sound.md#vsbp)                                                                    |
| VSMI | 3          | C->S      | soundsystem                                | [VSMI](sound.md#vsmi)                                                                    |
| VSND | 3          | C->S      | soundsystem                                | [VSND](sound.md#vsnd)                                                                    |
| VSPS | 3          | C->S      | soundsystem                                | [VSPS](sound.md#vsps)                                                                    |
