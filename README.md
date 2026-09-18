# EVCLI

One simulated electric vehicle, with a web interface, until Ctrl+C.

It is the counterpart of
[ChargingStationCLI](https://github.com/OpenChargingCloud/ChargingStation) and
is built the same way: a [Hermod](https://github.com/Vanaheimr/Hermod) HTTP
server carrying a JSON API and one Server-Sent Events stream, and a web
interface built by npm and embedded into the assembly, so that the vehicle is
one thing to deploy and needs nothing installed beside it.

Sign in, open **ISO 15118** and press *Look for a station*: the vehicle
multicasts an SDP request to `ff02::1` on the interface the station is on.
Then open **Charging** and press *Charge*: it connects, agrees on a protocol,
and charges to `SessionStop` — ISO 15118-2 or -20, AC, DC or MCS, plain TCP or
TLS, EIM or Plug & Charge.

Every request that goes out and every answer that comes in appears in the log
while it happens, so the **Logs** page shows the exchange itself rather than
only its outcome. `--sdp`, `--slac` and `--charge` do the same things once at a
start, from the console.

A vehicle is not a station turned around. A station runs a loop: it listens,
and answers whoever plugs in. A vehicle's flow has a beginning and an end —
arrive, discover, charge, leave — and nothing goes out on the wire until
somebody says so.


### Getting it

The libraries it is built from are submodules, so they have to come along:

```
git clone --recurse-submodules <this repository>
```

If you already cloned it without them:

```
git submodule update --init --recursive
```

They are fetched from GitHub over https, so nothing but git is needed - no
account, no key.

**On Windows**, turn long paths on first:

```
git config --global core.longpaths true
```

The deepest file in the submodules is 143 characters below the clone root, so
under the classic 260-character limit the root has about 115 characters to
live in. `D:\src\EVCLI` is fine; a checkout somewhere below
`C:\Users\<you>\AppData\Local\Temp\...` is not, and the clone fails halfway
through a submodule with `Filename too long` rather than at the start.
Per clone instead of globally: `git clone -c core.longpaths=true ...`.


### The ISO 15118 schemas

They are not in this repository, and nothing that touches ISO 15118 builds
without them. They are ISO's, published under a licence that grants use and not
redistribution, so fetching them is something you do rather than something we
ship:

```
bash libs/WWCP_ISO15118/tools/download-schemas.sh
```

One command, needs `curl` and `unzip`, idempotent. The reasoning is in
`libs/WWCP_ISO15118/SCHEMAS.md`.


### Building and running

```
dotnet build EVCLI.slnx
dotnet run --project EVCLI
```

The build needs the .NET 10 SDK and Node.js. `dotnet build
-p:SkipFrontendBuild=true` leaves the npm step out and reuses whatever is in
`libs/EV/EV/Frontend/dist`.

The first start makes up one account, `root`, keeps it under `accounts/`
beside the solution and prints its password once. Signing in happens at
Hermod's HTTPExt API, mounted under `/ext`. The web interface is
on <http://127.0.0.1:2347/>; `--any` binds every address instead of the
loopback, and `--help` lists the rest.

To watch a whole session without a real station, point it at the reference one
in the ISO 15118 submodule:

```
dotnet run --project libs/WWCP_ISO15118/WWCP_ISO15118_SECC -- --listen 15118
```

```
dotnet run --project EVCLI -- --connect "[::1]:15118" --max-charging-time 20m --charge
```

One iteration of the charge loop is one simulated minute, so a full charge is
several hundred exchanges — which is why the second command names a charging
time. IPv6 literals must be bracketed: `[fe80::1%eth0]:15118`. An unbracketed
`::1:15118` is a valid address in its own right, so splitting it at the last
colon would connect somewhere else entirely.

The four certificate passwords are read from `EV_VEHICLE_CERT_PASSWORD`,
`EV_CONTRACT_CERT_PASSWORD`, `EV_OEM_CERT_PASSWORD` and
`EV_TARIFF_CERT_PASSWORD`. They are never written to the configuration file. A
password given as a switch instead stands in the process list for every other
user of the machine.

While working on the web interface, run `npm run watch` in
`libs/EV/EV/Frontend` and start the vehicle with `--frontend
libs/EV/EV/Frontend/dist`: a reload in the browser then shows the change,
without rebuilding the C# side.


### Where things are

| | |
|---|---|
| `EVCLI/` | the command line: switches, and what the console says at a start |
| `libs/EV/EV/` | the vehicle itself - its configuration, its log, its JSON API, its web interface |
| `libs/EV/EV/Frontend/` | the web interface: TypeScript and SCSS, bundled by webpack |
| `libs/EV/EVTests/` | what the configuration may say, and what it may not |
| `libs/WWCP_ISO15118/` | the protocol: SDP, SLAC, V2GTP, the EXI codec, the session state machines |

The command line is this program's vocabulary and nothing else. What a vehicle
*is*, and what it does, lives in `libs/EV`.


### Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
