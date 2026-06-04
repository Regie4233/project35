# AI Terrain Diffusion Setup Guide

This project uses the `terrain-diffusion` HuggingFace model to generate highly realistic, natural terrain maps for the Unity voxel engine.

If you are cloning this project to a new machine, you will need to re-install the Python dependencies to generate new heightmaps. The generated maps themselves (like `world_map.raw`) are ignored in `.gitignore` because they can be extremely large, so you must generate them locally!

## Prerequisites

1. **Python 3.10+**: Ensure you have Python installed on your new machine.
2. **NVIDIA GPU (Recommended)**: For fast generation, a CUDA-compatible GPU is highly recommended.

## Installation Steps

1. Open a terminal (Powershell or Command Prompt) and navigate to the `TerrainServer` directory in the project:
   ```powershell
   cd path/to/project35/TerrainServer
   ```

2. Create a new Python virtual environment:
   ```powershell
   python -m venv .venv
   ```

3. Activate the virtual environment:
   - On Windows (Powershell):
     ```powershell
     .\.venv\Scripts\Activate.ps1
     ```
   - On Mac/Linux:
     ```bash
     source .venv/bin/activate
     ```

4. Install PyTorch (with CUDA support if you have an NVIDIA GPU):
   ```powershell
   pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu121
   ```
   *(If you don't have an NVIDIA GPU, just run `pip install torch` instead).*

5. Install the required dependencies for the terrain generator:
   ```powershell
   pip install -r requirements.txt
   pip install Pillow
   ```

## Generating a Terrain Map

Once installed, you can generate a new terrain map by running the `generate_map.py` script. The script accepts a `--size` argument which dictates the dimensions of the map (e.g. 1024, 2048, 4096, 8192).

Make sure your virtual environment is activated, then run:

```powershell
python generate_map.py --size 1024
```

This will automatically:
1. Download the `xandergos/terrain-diffusion-30m` model from HuggingFace (on first run).
2. Generate a `world_map.raw` binary file in `Assets/StreamingAssets/`.
3. Generate a `world_map.jpg` image in the same folder so you can preview it.

## Loading the Map in Unity

1. Open your Unity project.
2. Make sure your scene has the **Voxel World Settings** (or an empty GameObject) with the `SentisHeightmapGenerator` component attached.
3. The script dynamically calculates the map resolution from the `.raw` file, so you do not need to type the size anywhere!
4. **Important:** Select the `Voxel World Settings` GameObject and set the **Noise Scale** property based on your map size:
   - For `--size 1024`: Set `NoiseScale` to `0.00097` (1 / 1024)
   - For `--size 2048`: Set `NoiseScale` to `0.00048` (1 / 2048)
   - For `--size 4096`: Set `NoiseScale` to `0.00024` (1 / 4096)
   - For `--size 8192`: Set `NoiseScale` to `0.00012` (1 / 8192)

Press **Play** in the Unity Editor, and your beautiful AI-generated world will spawn in!
