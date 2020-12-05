# OSMPbfWebAPI
An OSM Pbf manager exposed as HTTP REST API

In order to build this you'll need to docker.
Run `docker build -t docker build -t osmctools-webapi .` to build.
When build is complete run `docker run -p 11911:80 osmctools-webapi` and surf to `localhost:11911/swagger`
