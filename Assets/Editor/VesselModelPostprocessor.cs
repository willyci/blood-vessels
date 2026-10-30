using UnityEditor;

// Import rules for the vessel meshes in Assets/Vessels/Models: the source files are in
// millimetres, so scale to metres; no materials (the scene assigns its own translucent ones);
// readable so runtime code can sample the mesh later.
public class VesselModelPostprocessor : AssetPostprocessor
{
    const string ModelFolder = "Assets/Vessels/Models/";

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(ModelFolder)) return;
        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 0.001f;
        importer.useFileScale = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.isReadable = true;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
    }
}
