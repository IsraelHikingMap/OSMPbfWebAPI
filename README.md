# OSMPbfWebAPI
An OSM Pbf manager exposed as HTTP REST API

This docker uses [OSM-C-Tools](https://gitlab.com/osm-c-tools/osmctools) under the hood and Asp.Net to serve an HTTP server.
The main functionality is by creating a container that hold an OSM pbf file.
Update it by downloading daily and or minutely updates and stream it back.

In order to build this you'll need to docker.
Run `docker build -t osmctools-webapi .` to build.
When build is complete run `docker run -p 11911:80 osmctools-webapi` and surf to `localhost:11911/swagger`

Now surf to localhost:11911/swagger/ to get a simple UI to interact with the pbf container mamanger

This is also available in dockerhub: `israelhikingmap/osmctoolswebapi`