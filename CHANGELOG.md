# 0.0.3 - initial package release
Initial release of the package version.

Changes from previous project version:
* No longer dependent on SerializedDictionary by AYellowPage, custom solution used instead to avoid this dependency.
* Fixed _Ground4 (alpha channel) texture not working due to being marked as global in shader graph.

ToDo for future releases:
* Track down the issue where terrain doesn't generate correctly in certain cases
* Look into using unity jobs/DOTs for faster mesh generation 
* Better terrain texture handling. More layers and ability to change how different textures transition into others
* Mesh simplification post-process step after generating mesh to avoid unneccesary geometry