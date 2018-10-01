#!/bin/bash
set -e

###########################################################################
# Used as fix-uid-gid solution in docker, almost copied from:
# https://github.com/tomzo/docker-uid-gid-fix/blob/master/fix-uid-gid.sh
###########################################################################

# This is the directory we expect to be mounted as docker volume.
# From that directory we know uid and gid.
DIRECTORY="/var/baget/packages"
OWNER_USERNAME="baget"
OWNER_GROUPNAME="baget"

if [ ! -d "$DIRECTORY" ]; then
  echo "$DIRECTORY does not exist, expected to be mounted as docker volume"
  exit 1;
fi

ret=false
getent passwd $OWNER_USERNAME >/dev/null 2>&1 && ret=true

if ! $ret; then
    echo "User $OWNER_USERNAME does not exist"
    exit 1;
fi
ret=false
getent passwd $OWNER_GROUPNAME >/dev/null 2>&1 && ret=true
if ! $ret; then
    echo "Group $OWNER_GROUPNAME does not exist"
    exit 1;
fi

NEWUID=$(ls --numeric-uid-gid -d $DIRECTORY | awk '{ print $3 }')
NEWGID=$(ls --numeric-uid-gid -d $DIRECTORY | awk '{ print $4 }')
OLDUID=$(id -u baget)
OLDGID=$(id -g baget)

if [[ $NEWUID != $OLDUID && $NEWUID != 0 ]]; then
  usermod -u $NEWUID $OWNER_USERNAME
fi
if [[ $NEWGID != $OLDGID && $NEWGID != 0 ]]; then
  groupmod -g $NEWGID $OWNER_GROUPNAME
fi
chown $NEWUID:$NEWGID -R /home/baget

###########################################################################
# Start server
###########################################################################

cd /app
if [[ $NEWGID != 0 ]]; then
  exec sudo -u baget -E -H dotnet /app/BaGet.dll
else
  echo "WARNING: running baget as root"
  exec dotnet /app/BaGet.dll
fi
