# Linux Test Environment

This documentation describes how you can set up a *Linux Test Environment* for testing this virtual electric vehicle instance against e.g. a virtual charging station.

## Creating Linux virtual block devices

```
mkdir -p /home/KVMGuests/ev
qemu-img create -f qcow2 /home/KVMGuests/ev/ev.qcow2 32G
qemu-img create -f raw   /home/KVMGuests/ev/ev.swap   8G
```


## Linux KVM script

The following linux script will set up a Linux KVM virtual machine with two network interfaces.    
The install image will be **Debian GNU/Linux** booted from a minimal CD image. When you install Debian it is needed to set the `bootindex` correctly (1 vs. 2 qcow2 image vs. 2 vs. 1 for the virtual CD drive). It is also
recommended to disable the second network interface within the script while installing.

You can access the VM via `telnet 127.0.0.1 4100`, or via `vncviewer 127.0.0.1::6000`.    
During Debian GNU/Linux installation is is recommended to use *vnc*.

```
#!/bin/bash
set -euo pipefail

NAME=ev
BASE=/home/KVMGuests/${NAME}

# pro VM eindeutig halten
VNC_DISPLAY=100          # 127.0.0.1:5900+DISPLAY
SERIAL_PORT=4100
MAC0=00:23:05:42:01:00
MAC1=00:23:05:42:01:01

qemu-system-x86_64 \
  -enable-kvm \
  -machine q35,accel=kvm \
  -cpu host \
  -name "${NAME}",process="${NAME}" \
  -pidfile "${BASE}/pid" \
  -m 4096 \
  -smp 4 \
  -k de \
  \
  -display none \
  -vnc "127.0.0.1:${VNC_DISPLAY}" \
  \
  -serial "telnet:127.0.0.1:${SERIAL_PORT},server,nowait,nodelay" \
  \
  -drive if=none,id=disk0,file="${BASE}/${NAME}.qcow2",format=qcow2 \
  -device virtio-blk-pci,drive=disk0,bootindex=1 \
  \
  -drive if=none,id=swap0,file="${BASE}/${NAME}.swap",format=raw \
  -device virtio-blk-pci,drive=swap0 \
  \
  -drive if=none,id=cd0,media=cdrom,readonly=on,file=/home/KVMGuests/debian-13.7.0-amd64-netinst.iso \
  -device ide-cd,bus=ide.0,drive=cd0,bootindex=2 \
  \
  -netdev tap,id=net0,ifname="tap1${NAME}",vhost=on,script=/home/KVMGuests/addInterfaceToBridge.sh,downscript=/home/KVMGuests/removeInterfaceFromBridge.sh \
  -device virtio-net-pci,netdev=net0,mac="${MAC0}" \
  \
  -netdev tap,id=net1,ifname="tap2${NAME}",vhost=on,script=/home/KVMGuests/addInterfaceToBridge.sh,downscript=/home/KVMGuests/removeInterfaceFromBridge.sh \
  -device virtio-net-pci,netdev=net1,mac="${MAC1}" \
  \
  -boot menu=on \
  -daemonize
```


## Installing Debian GNU/Linux

1. Installing Debian GNU/Linux 13.7.0: https://www.debian.org/CD/netinst/ for AMD64.
2. Boot the virtual machine
3. Deselect everything except `Standard Tools`, select `SSH server`
4. `vncviewer 127.0.0.1::6000`
5. `apt install joe mc sudo net-tools git`
6. `joe /etc/sudoers` add: `ahzf    ALL=(ALL:ALL) NOPASSWD: ALL`, or which user you prefer :)
7. https://learn.microsoft.com/en-us/dotnet/core/install/linux-debian?tabs=dotnet10
8. `apt install -y curl ca-certificates unzip gnupg`
9. `curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -`
10. `sudo apt install -y nodejs`


## Linux Virtual Bridges

We use two Linux virtual Ethernet bridges, one for management traffic and another one for the simulated ISO 15118 charging cable. As ISO 15118 is **IPv6-only** we do not configure any IPv4 for it. We also do not add the host machine to this Ethernet network. So for ISO 15118 CCS (Combined Charging System) this network will always just have two hosts - the EV and the charging station (EVSE). In contrast to this for ISO 15118 MCS (MegaWatt Charging) there might be additional hosts within this network.

```
auto br1
iface br1 inet static
        address         10.3.0.1
        netmask         255.255.0.0
        broadcast       10.3.255.255

        pre-up          /sbin/brctl addbr br1
        pre-up          /sbin/brctl setfd br1 0
        post-down       /sbin/brctl delbr br1

        up              /bin/echo "1" > /proc/sys/net/ipv4/ip_forward
        up              /bin/echo "1" > /proc/sys/net/ipv6/conf/all/forwarding
        up              /sbin/iptables -t nat -A POSTROUTING -s 10.3.0.0/16  -j MASQUERADE


auto br2
iface br2 inet6 manual

        pre-up          /sbin/brctl addbr br2
        pre-up          /sbin/brctl setfd br2 0
        post-down       /sbin/brctl delbr br2

        up              /bin/echo "0" > /sys/class/net/br2/bridge/multicast_snooping
        up              /bin/echo "1" > /proc/sys/net/ipv4/ip_forward
        up              /bin/echo "1" > /proc/sys/net/ipv6/conf/all/forwarding
        up              /bin/echo "1" > /proc/sys/net/ipv6/conf/br2/disable_ipv6
```


Helper script: `/home/KVMGuests/addInterfaceToBridge.sh`
```
#!/bin/sh

bridge=br`echo $1 | awk '{split($1,a,"[A-Za-z_\\\-]+"); print a[2]}'`

echo ""
echo "add $1 to $bridge using $0"

/sbin/ifconfig $1 0.0.0.0 up
/sbin/brctl addif $bridge $1

exit 0
```

Helper script: `/home/KVMGuests/removeInterfaceFromBridge.sh`
```
#!/bin/sh

bridge=br`echo $1 | awk '{split($1,a,"[A-Za-z_\\\-]+"); print a[2]}'`

echo ""
echo "remove $1 from $bridge using $0"

/sbin/brctl delif $bridge $1
/sbin/ifconfig $1 0.0.0.0 down

exit 0
```


## Linux VM Network Settings

The networking settings of the virtual machine can be set via `/etc/network/interfaces`:

```
# The primary management network interface
allow-hotplug enp0s4
iface enp0s4 inet dhcp

# The ISO 15118 network cable
allow-hotplug enp0s5
iface enp0s5 inet manual
```
