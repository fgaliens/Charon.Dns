#!/bin/sh
cd ..;
docker build --tag charon.dns:latest . || exit; 
docker run -p 53:53/udp -p 53:53/tcp charon.dns:latest --LogLevel=Warning --FileLogLevel=Warning;