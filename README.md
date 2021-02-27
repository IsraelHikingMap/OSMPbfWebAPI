# OSMPbfWebAPI
An OSM Pbf manager exposed as HTTP REST API

This docker uses [OSM-C-Tools](https://gitlab.com/osm-c-tools/osmctools) under the hood and Asp.Net to serve an HTTP server.
The main functionality is by creating an extract that holds an OSM pbf file.
Update it by downloading daily and or minutely updates and stream it back.

In order to use this you'll need docker.

## Runing with `docker-compose`
Run  `docker-compose up -d`

## Running with `docker`
Build the container with `docker build . -t osmpbfwebapi`
When build is complete run `docker run -p 8987:80 osmpbfwebapi`

## Checking the API
Open [`http://localhost:8987/swagger`](http://localhost:8987/swagger) to get a simple UI to interact with the pbf extracts mamanger/

This is also available in dockerhub: `israelhikingmap/osmctoolswebapi`
