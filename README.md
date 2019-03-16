# BinaryDependencyPropagator


What it is:
This tool is meant to copy dll files between projects to facilitate debugging of .net applications.
It is useful when a project is composed of separate solutions requiring manual copying of binaries between the directories.
For example, many projects publish their binaries into a private NuGet repository and at a later stage of the build process
they download these binary dependencies in a different project.
To make debugging easier, you can locally use this tool to override your dll-s with the other dll-s you build on your machine
but including your own debugging symbols. This tool automates this process with the assumption that the dll-s with the newest
modification date should override the older dll-s with the same name.

Usage:
Modify the directory names in the main method to match your project directories.
Run it every time after you build one of the projects of your application that you would like to propagate
to the other parts of your project.

WARNING:
Think twice if this tool is the right choice in your case. It overrides *.dll files so YOU WILL LOSE some of your *.dll-s as
they will be overridden by the newer versions of these dll-s

WARNING-2:
This tool has not been extensively tested. It is a prototype and not a production-ready solution.

Think of it as a template for building your own solution of your own problem.
It is meant to be simple to modify so it looks more like a single-file script rather than a normal application.
