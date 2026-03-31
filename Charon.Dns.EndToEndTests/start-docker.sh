#!/bin/zsh
cd ..;
docker build --tag charon.dns:latest . || exit; 
docker run -p 53:53/udp charon.dns:latest;