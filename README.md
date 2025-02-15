# Stylized Terrain Editor
A custom terrain solution for stylized terrain in Unity, inspired by the work of t3ssel8r on youtube and based on godot code by Jackachulian on github.


# Installation
Package manager -> click plus icon -> add git url -> https://github.com/BradFitz66/Stylized-Terrain-URP.git 

# Usage
* Attach the MarchingSquaresTerrain script to an empty game object. Empty MUST be at 0,0,0 or it will cause issues.
* Put a material in the terrain material field on the Marching Squares Terrain script. You can make your own material (use stylized terrain shader, or make your own) or use the one that comes with the package called "Terrain". If you can't see the material, make sure you click the dashed out eye icon in the top right corner of the search dialog box.
* Use chunk tool to add new chunks. The first chunk can be added anywhere while subsequent chunks must be adjacent to an existing chunk
* Use sculpting and texturing tool to manipulate the terrain to your desire

# Bugs
There are certain conditions where the geometry won't generate properly and create a hole in the terrain. In these cases, just manipulate the affected geometry up and down a bit more until it fixes itself.
I'm not sure what causes this bug but I guess it's some sort of rounding issue in the code somewehre.

# Issues
* File system is a mess. Brush scripts are in runtime to avoid assembly definition issues, despite not being runtime scripts

# Credits
This project wouldn't have been possible without @Jackachulian, whose code I ported from Godot to Unity in order to make the mesh generator.

