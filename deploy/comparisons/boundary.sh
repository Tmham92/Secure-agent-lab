#!/bin/sh
set -eu
iptables -P OUTPUT DROP
ip6tables -P OUTPUT DROP
iptables -F OUTPUT
ip6tables -F OUTPUT
# Outer boundary stays intact even for the intentionally vulnerable worker.
iptables -A OUTPUT -m owner --uid-owner 1654 -p tcp -d 127.0.0.1 --dport 9000 -j ACCEPT
iptables -A OUTPUT -m owner --uid-owner 1654 -p tcp -d 127.0.0.1 --dport 8080 -j ACCEPT
iptables -A OUTPUT -m owner --uid-owner 1657 -p tcp -d 127.0.0.1 --dport 8080 -j ACCEPT
iptables -A OUTPUT -m owner --uid-owner 1655 -p tcp -d 127.0.0.1 --dport 5188 -j ACCEPT
iptables -A OUTPUT -m owner --uid-owner 1655 -p tcp -d 127.0.0.1 --sport 8080 -j ACCEPT
iptables -A OUTPUT -m owner --uid-owner 1656 -d 127.0.0.1 -j ACCEPT
touch /ready/installed
exec sleep infinity
