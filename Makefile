.PHONY: all clean

all:
	dotnet build src/*.slnx

clean: 
	rm -rf _artifacts/ _output/ _temp/ _tests/
