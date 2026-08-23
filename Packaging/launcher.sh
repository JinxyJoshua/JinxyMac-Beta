#!/bin/sh
#
# Picks the build that matches the machine.
#
# Two copies of the app ship inside this bundle, one per architecture,
# because a true universal binary needs Apple's lipo and this was built on
# a PC. The cost is bundle size; the benefit is that one download runs on
# both Apple silicon and Intel with nothing to choose.

here=$(cd "$(dirname "$0")" && pwd)

case "$(uname -m)" in
    arm64) exec "$here/arm64/JinxyMac" "$@" ;;
    *)     exec "$here/x64/JinxyMac" "$@" ;;
esac
