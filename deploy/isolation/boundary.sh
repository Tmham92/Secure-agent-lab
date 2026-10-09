#!/bin/sh
set -eu
# Trusted namespace guardian. Worker never receives NET_ADMIN, host mounts or Docker socket.
# Configure both families before publishing readiness. Failure leaves workloads unstarted.
iptables -P OUTPUT DROP
ip6tables -P OUTPUT DROP
iptables -F OUTPUT
ip6tables -F OUTPUT
# Worker can reach one local proposal relay only. No DNS, host gateway or remote ranges.
iptables -A OUTPUT -m owner --uid-owner 1654 -p tcp -d 127.0.0.1 --dport 8080 -j ACCEPT
# Relay can reach the fixed local backend and return replies to the worker only.
iptables -A OUTPUT -m owner --uid-owner 1655 -p tcp -d 127.0.0.1 --dport 5188 -j ACCEPT
iptables -A OUTPUT -m owner --uid-owner 1655 -p tcp -d 127.0.0.1 --sport 8080 -j ACCEPT
# Gateway and trusted operator share a dedicated UID and use synthetic loopback tools.
iptables -A OUTPUT -m owner --uid-owner 1656 -d 127.0.0.1 -j ACCEPT
ip6tables -A OUTPUT -m owner --uid-owner 1656 -d ::1 -j ACCEPT
touch /ready/installed
exec sleep infinity
