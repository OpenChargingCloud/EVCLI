# EVCLI

[![CI](https://github.com/OpenChargingCloud/EVCLI/actions/workflows/ci.yml/badge.svg)](https://github.com/OpenChargingCloud/EVCLI/actions/workflows/ci.yml)
[![Nightly](https://github.com/OpenChargingCloud/EVCLI/actions/workflows/nightly.yml/badge.svg)](https://github.com/OpenChargingCloud/EVCLI/actions/workflows/nightly.yml)

One simulated electric vehicle, with a web interface and a prompt, until
Ctrl+C.

It is the counterpart of
[ChargingStationCLI](https://github.com/OpenChargingCloud/ChargingStation) and
is built the same way: a [Hermod](https://github.com/Vanaheimr/Hermod) HTTP
server carrying a JSON API and one Server-Sent Events stream, and a web
interface built by npm and embedded into the assembly, so that the vehicle is
one thing to deploy and needs nothing installed beside it.

Sign in, and the first page is **Charging**: choose how the vehicle plugs
in — directly, over SLAC, or onto the 10BASE-T1S bus of an MCS coupler — and
press *Look for a station*. It plugs in, stays plugged in, and multicasts an
SDP request to `ff02::1` on the interface the station is on. Every station
that answers is offered; choose how to secure the session beside it and press
*Charge here*: the vehicle connects over the link it holds, agrees on a
protocol, and charges to `SessionStop` — ISO 15118-2 or -20, AC, DC or MCS,
plain TCP or TLS, EIM or Plug & Charge.

Every request that goes out and every answer that comes in appears in the log
while it happens, so the **Logs** page shows the exchange itself rather than
only its outcome. `--sdp`, `--slac`, `--t1s` and `--charge` do the same things
once at a start, from the console, and `discover` does it again whenever you
type it at the vehicle's prompt.

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

The deepest file in the submodules is 110 characters below the clone root, and
under the classic 260-character limit git creates no file whose whole path is
longer than 259, so the root itself may be at most 148 characters long.
`D:\src\EVCLI` is fine; a checkout nested deep below
`C:\Users\<you>\AppData\Local\Temp\...` can run out of room, and then the
clone fails halfway through a submodule with `Filename too long` rather than
at the start. Per clone instead of globally: `git clone -c core.longpaths=true ...`.


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

**Recommended for the first start: give `root` your own SSH key** with it, so
that you can type at the vehicle over SSH from the start - see
[Typing at it over SSH](#typing-at-it-over-ssh):

```
dotnet run --project EVCLI -- --authorize-ssh-key root=C:\Users\you\.ssh\id_ed25519.pub
```

Without it, the first start makes up a key pair for `root` and prints its
private key once, below the password.

A switch that cannot be followed ends the start with exit code 2 and says why
on the error stream, in lines of 80 columns as `--help` is. So does a
certificate to import that would not go in — no certificate in the file, a
PKCS#12 whose password was not given, a credential without its key — and that
is found out before the vehicle is made, so that a first start refused for it
leaves nothing on the disk.

Everything that happens is written three times over, because the three answer
different questions. The **console** shows what is going on to whoever is
watching, at the level `--verbose` and `--quiet` choose. The **Logs** page
keeps the last two thousand entries for whoever asks, and loses them when the
process ends. And `logs/` beside the solution keeps one file per day, every
entry down to the debug ones, for the afternoon somebody asks what happened
last night — `--log-file <dir>` puts it elsewhere, `--no-log-file` leaves it
out, and nothing in it is ever deleted. Which directory it is, the start says
under `log files`, and the Configuration page on its Event log card.

Below it, `metrological/` is the vehicle's log book: what bears on the time it
stamps things with and on what it trusts. Every start, every synchronisation
with what each time server answered, what was news about a time server's
certificate, every change of its time servers and of its certificates, one
line after the other, each pointing back at the one before and signed with a
key kept beside them. A file per day, never thinned out. Without log files
there is no log book either.

The days are UTC days, as the timestamps in the files are. A file that cannot
be written is said once on the console rather than once per entry, every entry
after that is tried again, and the first one that makes it is preceded by a
line saying how many are missing.

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

That station runs beside the vehicle on loopback, which is everything for the
message exchange and nothing at all for the wire: SDP discovery, link-local
addressing and the interface the powerline modem sits on only mean something
between two machines sharing one Ethernet segment.
[LinuxTestEnvironment.md](LinuxTestEnvironment.md) sets that up — KVM guests on
a Linux host, a bridge for management and a second, IPv6-only bridge standing
in for the charging cable.

### Typing at it

Once it is up, the console is a prompt named after the vehicle rather than a
place that only scrolls:

```
EV> discover
a station at [fe80::223:5ff:fe42:201%5]:15118 (TLS), after 1 request(s) in 42 ms
```

`help` lists what can be typed, `quit` leaves, **Tab** completes and **↑**
walks back through what was typed before. `discover` is the same discovery the
**ISO 15118** page runs and the same one `--sdp` runs once at a start — three
ways of asking, one implementation, so they cannot disagree about what
happened.

`syncNTS` is **Sync now** from the **NTS client** page in the same way: the
same time servers asked, the same entries in the log, and afterwards the same
result on the page as its last synchronisation. The one line that differs is
the one saying who asked — the page names the account that pressed the button
and tags it `web`, the prompt says it was the command line and tags it `cli`.
Like the button, it asks and reports and leaves the clock alone. The console
gets a line for each server as well, because the log only records what the
group concluded:

```
EV> syncNTS
succeeded after 673 ms: 4 of 4 server(s) answered (2 required), offset +702.4 ms, spread 0.5 ms
  ptbtime1.ptb.de  +702.4 ms, round trip 38.1 ms, key exchange new
  ptbtime2.ptb.de  +702.7 ms, round trip 38.0 ms, key exchange new
  ptbtime3.ptb.de  +702.4 ms, round trip 38.0 ms, key exchange new
  ptbtime4.ptb.de  +702.2 ms, round trip 39.3 ms, key exchange new
```

With one of the vehicle's time servers after it, it is that server's **Test**
button instead: one server, on the ports it is configured with, and every step
of the key exchange and the time request with when it happened. Only a server
of this vehicle is tested; anything else is answered with the ones there are,
and nothing is asked.

The key exchange is TLS, and the test says what its certificate claims and
whether that held up: the session, then every certificate of the chain as this
machine built it - the server's, the intermediates', the root's - each with
both ends of its validity and the days it has left, the root's SHA-256
fingerprint, and the verdict with its reasons. The root is there as much as the
server's certificate because a root can be pinned, and a pinned root that runs
out stops everything relying on it; which root the chain ends at depends on the
machine's trust store. A certificate that is refused is described just the
same, before the exchange is said to have failed.

```
EV> syncNTS ptbtime2.ptb.de
ptbtime2.ptb.de answered, 578 ms altogether:
    +2 ms  Asking ptbtime2.ptb.de: key exchange on port 4460, time on port 123, 10 second(s) allowed.
   +63 ms  'ptbtime2.ptb.de' resolves to 192.53.103.104, 2001:0638:0610:be01:0000:0000:0000:0104.
   +63 ms  Key exchange over TLS ...
  +518 ms  Connected to 2001:0638:0610:be01:0000:0000:0000:0104, of 2 address(es) that were offered.
  +518 ms  Where the time went: name 15 ms, TCP 28 ms, TLS 339 ms, key exchange 43 ms.
  +520 ms  TLS 1.3, TLS_AES_128_GCM_SHA256, ALPN ntske/1.
  +523 ms  Server certificate: CN=ptbtime2.ptb.de, for ptbtime2.ptb.de; RSA 3072-bit, sha256RSA; valid 2026-08-09 03:05:52 to 2026-11-07 03:05:51 UTC, 44 day(s) left.
  +523 ms  Intermediate CA: CN=YR1, O=Let's Encrypt, C=US; RSA 2048-bit, sha256RSA; valid 2025-09-03 00:00:00 to 2028-09-02 23:59:59 UTC, 710 day(s) left.
  +523 ms  Intermediate CA: CN=Root YR, O=ISRG, C=US; RSA 4096-bit, sha256RSA; valid 2026-05-13 00:00:00 to 2032-09-02 23:59:59 UTC, 2171 day(s) left.
  +524 ms  Root CA: CN=ISRG Root X1, O=Internet Security Research Group, C=US; RSA 4096-bit, sha256RSA; valid 2015-06-04 11:04:38 to 2035-06-04 11:04:38 UTC, 3175 day(s) left.
  +524 ms  The root's SHA-256 fingerprint: 96bcec06264976f37460779acf28c5a7cfe8a3c0aae11a8ffcee05c0bddf08c6.
  +524 ms  Validated: the chain ends at a root this machine trusts, nothing in it is revoked (asked online), and 'ptbtime2.ptb.de' is one of the server certificate's names.
  +525 ms  The key exchange succeeded: AES_SIV_CMAC_256, 8 cookie(s).
  +525 ms  It named no NTP server of its own, so the time is asked of this host.
  +525 ms  Authenticated NTP request ...
  +577 ms  Answered by [2001:638:610:be01::104]:123; 8 cookie(s) left, and a fresh one came back.
  +578 ms  Round trip 23.3 ms.
  +578 ms  This vehicle's clock is +807.9 ms off what ptbtime2.ptb.de says.
  +578 ms  The clock was not stepped: that is a different thing, with meter readings and certificates hanging off it, and not something a test does by surprise.
```

Tab is the reason the prompt is worth having. `discover` takes an interface,
and an interface is called `enp0s5` on the vehicle's Debian and
`vEthernet (Default Switch)` on a Windows desk; nobody types either of those
from memory twice. The vehicle already knows which of its interfaces could
carry V2G traffic, so Tab offers exactly those — quoted where a name has a
space in it. `syncNTS` takes a time server, and Tab offers the vehicle's own.
Either list comes as soon as the command is typed, and each Tab after that
completes as far as the names agree.

The log keeps writing while you type, from whichever thread did the thing it is
reporting, and your half-typed line survives it: the line is taken off the
screen, the entry is written whole, and the line comes back with the cursor
where it was. Nothing is suppressed and nothing is held back to make that work.

Where there is no terminal — from a script, under a service manager, in CI, or
with the output going into a file or through `| tee` — there is no prompt and
nothing to type at, and the vehicle simply runs until it is stopped. Ctrl+C
stops it, and so does the SIGTERM a service manager sends: either way it shuts
down as it does after `quit`.

### Typing at it over SSH

The same prompt is served over SSH, on port 22347 — twenty thousand above the
web interface's — and on the addresses the web interface listens on: the
loopback, or every address with `--any`. Nothing else is: no shell of the
machine, no files, no tunnels. `--ssh-port` moves it, `--no-ssh` switches it
off.

Whoever signs in is an account of the vehicle, under its name, with a key of
its own. The first start makes `root`; give it your public key with that very
start - the way recommended:

```
dotnet run --project EVCLI -- --authorize-ssh-key root=C:\Users\you\.ssh\id_ed25519.pub
```

The private key then stays on your machine, and no console ever shows it. A
first start without `--authorize-ssh-key root=...` makes up a key pair for
`root` instead and prints its private key once, below the password in the
first-start box: save the lines from `-----BEGIN OPENSSH PRIVATE KEY-----` to
the END line as a file only you can read, and sign in with
`ssh -i <file> -p 22347 root@127.0.0.1`, or import the file in PuTTYgen for
PuTTY. Like the password it is kept nowhere - but a console may be kept, by a
service's journal or a redirected output; replace it with your own key and
take it out. `--authorize-ssh-key` also works at any later start.

An OpenSSH `.pub` goes in as it is, and so does what PuTTYgen saves with *Save
public key*. The key is kept with the account, beside its password, and the
`sshKeys` command manages it - at the console, or over SSH for the account
signed in:

```
sshKeys root
sshKeys root add ssh-ed25519 AAAA... you@laptop
sshKeys root remove SHA256:abc
```

`remove` takes the fingerprint `sshKeys` lists, or enough of its beginning, and
locks that key out at once. `apiKeys` does the same for the account's API keys,
and shows a new one once, when it is made.

Then sign in:

```
ssh -p 22347 root@127.0.0.1
```

or, in PuTTY, host `127.0.0.1`, port `22347`, *Connection → Data → Auto-login
username* `root`, and the private key under *Connection → SSH → Auth →
Credentials*. The first time, PuTTY asks whether to trust the vehicle's host
key: the banner prints its fingerprint under `SSH`, to compare it with.

```
Electric vehicle v0.1.0: signed in as 'root' from 127.0.0.1:55351.
Type 'help' for what can be typed here; 'quit' or Ctrl+D leaves, and the electric vehicle keeps running.

EV> syncNTS
```

Everything works as at the console — Tab, the history, the log above the line
being typed — with three differences. `quit`, `exit` and Ctrl+D leave the
session, and the vehicle keeps running. The account may do what its roles let
it do on the web interface, and the log names it: "'root' at the command line
over SSH asked this electric vehicle to synchronise its time.", tagged `cli`
and `ssh`. And the session's log starts at the console's level and is its own:
`log debug` shows everything here, `log off` nothing, for this session alone.
`who` says who else is signed in.

Tried from Debian's OpenSSH in a real pseudo terminal, with the screen read
back as an xterm would show it: Tab, Backspace as DEL, the arrows, Home and End
in both of their spellings, Delete, Ctrl+C on a half-typed line, a window
narrowed to 40 columns, keys typed ahead while a command runs, `exit`, and
SIGTERM stopping the vehicle afterwards.

### Certificates

Everything this vehicle believes, everything it presents and every server it
recognises lives in one store, `certificates/` beside the configuration file —
so beside the solution unless `--config` says otherwise — and is managed on the
**Certificates** page or from the command line. A certificate is put there once
and then chosen by a short handle, so the same contract can be kept beside
three others and switched between runs.

The store is the directory: one file per certificate below it, and an
`index.json` recording what a file cannot say about itself: what somebody calls
it, whether it is switched on and - for a TLS root or a server certificate -
what it is kept for. So a store copied to another machine arrives complete,
and a lost index costs labels, switches and usages rather than certificates.

There are three kinds of root, kept apart rather than pooled, because they
answer three different questions: a **v2gRoot** says which station may be at
the other end of the cable, an **moRoot** says which contract certificate is
worth paying with, and an **oemRoot** says which vehicle a station should issue
a contract to. One bag of roots would let an OEM root vouch for a contract.
Every switched-on root of a kind is believed at once; none of them is chosen
per session.

A fourth kind of root, **tlsRoot**, says which time server and which name
server over TLS or HTTPS may be believed, beside the roots of the machine the
vehicle runs on - and is told what it is for, `nts`, `dns` or both, because a
root kept for the name servers alone vouches for no time. A **tlsServer** is a
server's own certificate, kept so that the server can be held to it by its
fingerprint. Holding a server to a certificate or a root is said on the **NTS
client** and **DNS client** pages: a server's dialog takes the fingerprints,
offers the one it showed last and the ones the store keeps for it, and says
what a mismatch comes to and whether it is held to what it is first believed
with. What every server was last believed with is kept in `known-servers.json`
beside the configuration file - fingerprints and nothing else - so that
another certificate is noticed where a server is held to none.

The four credentials — `vehicle`, `contract`, `oemProvisioning` and
`tariffVerification` — are chosen one per session, on the Charging Session page or with
the switches below.

```
dotnet run --project EVCLI -- \
    --import-certificate v2gRoot=v2g-root.pem \
    --import-certificate contract=contract.p12 --certificate-password secret \
    --list-certificates
```

Importing a credential also chooses it; importing a root simply makes it
believed - a TLS root or a server certificate for every use, until the
Certificates page says what it is for. `--list-certificates` prints every
handle and marks the ones chosen, this run's switches included, and
`--certificates <dir>` points the vehicle at another store, measured from where
it is started.

PEM, DER and PKCS#12 all go in. A root is a certificate on its own; a
credential has to bring its private key, so a PEM for one holds the key
beside the certificate and the sub-CAs above it — the file `openssl` writes
when it is given all three. An encrypted key block is opened with the same
password a protected PKCS#12 would be. Certificates already in the store
directory — copied in by hand, restored from a backup — are read again at every
start and adopted, so putting a file there is a way to install it.

Switching a certificate off is not the same as deleting it: the first leaves
the file where it is, for the afternoon somebody takes a contract out of
service; the second deletes it, because a store whose "delete" left the private
key on the disk would be worse than one with no delete at all. Time switches a
certificate off as well, and separately — an expired certificate stays listed
and stops being used.

**The private keys in the store are not encrypted.** A PKCS#12 is opened with
its password once, at import, and written back without one, so that any number
of certificates per role work without any number of passwords to carry. What
guards them is the file system: the store directory is made for its owner alone
where the platform allows saying so in one call, which on Windows means the ACL
a new directory inherits and nothing more. Anybody who can read `certificates/`
can take this vehicle's identity and its contract, so it belongs on a machine
whose users are all trusted with exactly that. The vehicle says so at every
start, and at every import of a key.

A password for an import is read from `EV_CERT_PASSWORD` where
`--certificate-password` is not given. A password given as a switch stands in
the process list for every other user of the machine.

While working on the web interface, run `npm run watch` in
`libs/EV/EV/Frontend` and start the vehicle with `--frontend
libs/EV/EV/Frontend/dist`: a reload in the browser then shows the change,
without rebuilding the C# side.


### Where things are

| | |
|---|---|
| `EVCLI/` | the command line: the vehicle's own switches, and its lines of what the console says at a start |
| `EVCLI/CLI/` | what can be typed at the running vehicle - one file per command |
| `libs/EV/EV/` | the vehicle itself - its configuration, its log, its JSON API, its web interface |
| `libs/EV/EV/Frontend/` | the web interface: TypeScript and SCSS, bundled by webpack |
| `libs/WWCP_Node/` | what every one of these programs shares: the configuration file, the log, name resolution and the time, the certificate store, who may sign in, the command line over SSH, and the switches, the usage and the banner every node has |
| `libs/EV/EVTests/` | what the configuration may say, and what it may not - and, through WWCP_Node's conformance suite, what every node has to answer |
| `libs/WWCP_ISO15118/` | the protocol: SDP, SLAC, the 10BASE-T1S bus, V2GTP, the EXI codec, the session state machines |
| `LinuxTestEnvironment.md` | two virtual machines and two bridges, for a session over a wire rather than over loopback |
| `.github/workflows/` | what runs on every push, and what runs at night |

The command line is this program's vocabulary and nothing else - the switches
it is started with and the commands it can be typed at. What a vehicle *is*,
and what it does, lives in `libs/EV`.


### Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
