# Stylized Terrain Editor
A custom terrain solution for stylized terrain in Unity, inspired by the work of t3ssel8r on youtube and based on godot code by Jackachulian on github.


# Installation
Package manager -> click plus icon -> add git url -> https://github.com/BradFitz66/Stylized-Terrain-URP.git 

# Usage
* Attach the MarchingSquaresTerrain script to an empty game object. 
* Use chunk tool to add new chunks. The first chunk can be added anywhere while subsequent chunks must be adjacent to an existing chunk
* Use sculpting and texturing tool to manipulate the terrain to your desire

# Bugs
There are certain conditions where the geometry won't generate properly and create a hole in the terrain. In these cases, just manipulate the affected geometry up and down a bit more until it fixes itself.
I'm not sure what causes this bug but I guess it's some sort of rounding issue in the code somewehre.


# Credits
This project wouldn't have been possible without @Jackachulian, whose code I ported from Godot to Unity in order to make the mesh generator.

