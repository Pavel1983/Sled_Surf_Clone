# Sled Surf prototype

A playable downhill run in the style of Sled Surfers, built with the Miraculous Ladybug asset pack.

## Unity version

Unity `6000.5.11f1` with the Universal Render Pipeline and the Input System.

## How to run

1. Open the project in that Unity version.
2. Open `Assets/_Game/Scenes/SampleScene.unity`.
3. Press Play.
4. On the start screen, buy upgrades if you have coins, then press **TAP TO PLAY**.
5. Drag back from the sled to pull the slingshot, then release. A longer pull launches faster. The shot stays within 20 degrees of the road.
6. During the run, press and drag anywhere on the screen. The stick appears under the finger and steers left and right. Steering is stronger on the snow than in the air.
7. When the run ends, **CONTINUE** returns to the start. Coins and upgrade levels stay.

The sled is the object named `Slider`. The road is the `Level1_Road` prefab.

## What the run contains

- Launch from a slingshot, then a physics slide down a spline road. The body does not roll. Friction comes from the snow mask: packed snow is slower, cleared ice is faster.
- Steering with a floating stick.
- Coins along the path. Driving through one collects it.
- Sounds from the Ladybug pack: obstacle impacts, her voice on a hit, the sit-down, a sharp turn on the snow, a landing, a coin pickup and button taps.
- Three obstacles, placed by hand in the scene. An iceberg and an electric gate end the run. A frozen bench keeps about a third of the current speed. Prefabs are in `Assets/_Game/Prefabs/Obstacles/Crash` and `.../Slow`.
- Street props from the Ladybug pack along both sides of the run, and each one is an obstacle. Buses, news vans and hedges end the run. Flower beds, flower carts, barriers and pizza signs break and keep about a third of the speed. Only the props near the sled are switched on.
- The run also ends if the sled stops, if it starts sliding backward, or when it reaches the finish.
- The finish is **70% of the spline**, not a shorter mesh. `finishFraction` on `Slider` changes that. Set it to `1` to require the whole road.
- After the run, a results card shows distance and coins earned. Upgrades bought before the next launch are saved with `PlayerPrefs`: slingshot power, lower sled friction, and coins paid per kilometre.

## Project layout

Everything the game uses is under `Assets/_Game`.

- `Scenes` holds the one scene, `Settings` the render pipeline and input assets.
- `Scripts` holds the code, split by layer:
  - `Core` is plain C#: the coin and upgrade rules, and the slingshot aim maths.
  - `Data` is the saved progress and the repository that reads and writes it.
  - `Game` is the objects of the scene: the road, the sled, the rider, coins, obstacles, camera, sound, and the composition root `GameBootstrap`.
  - `UI` is the HUD and the steer stick.
  - `Editor` is the level tools.
- `Art` holds the Ladybug model and clips, the obstacle meshes, the road texture and the HUD sprites. `Audio` holds the sound clips and the `RunSounds` asset that lists them.
- `Prefabs` holds the road and the obstacles. `Materials` and `Shaders` hold the snow road shader and the flat materials.

`TechDesign.md` describes the architecture. `CodeRules.md` holds the code and architecture rules, and `.editorconfig` enforces the formatting part of them.

Level tools, in the Unity menu and the Scene view toolbar:

- **Road Sculpt** and **Road Snow** raise the surface and paint the snow mask on the selected road.
- **Tools → Road → Snap Selection To Road** seats the selected props on the surface and tilts them to it.

## Implementation decisions

- The road is generated from a Unity spline. Width, bowls, and the snow mask live on `SplineRoad`. The sled is a rigidbody with a capsule collider that lies along the direction of travel, because she rides lying down. The solver never rotates it: the heading and the slope set its rotation, so the character cannot tumble when the slope changes.
- Ladybug is parented to the sled and animated with clips from the pack through a Playable graph. She stands facing the camera on the start screen, turns and sits down on **TAP TO PLAY**, slides, and falls when she crashes. The results card waits two seconds for that fall.
- Coins are placed in code along the spline and only activated near the sled. The ribbon is several kilometres long, so keeping every prop awake was too expensive.
- Obstacles are prefabs placed by hand under the `Obstacles` object of the scene, so the level layout is designed rather than generated. An obstacle outside that object does not come back after a restart. `Obstacle` is the shared base: it checks the hit, fires once per run and resets on the next run. `CrashObstacle` and `SlowObstacle` only define the effect, so a new obstacle type is one small subclass.
- Obstacles are solid colliders, and the sled reports the touch from its own collision callback. A jump can clear an obstacle, and the collider can sit on any child of the prefab.
- Sounds are played by one listener, `RunAudio`. The sled, the coins and the HUD only raise events, and the clips live in one `RunSounds` asset.
- `GameBootstrap` is the one composition root. It loads the saved progress, hands the economy to the sled and the HUD, and connects the events. Nothing looks other objects up at runtime: references are set in the inspector.
- The HUD matches the Sled Surfers reference: pull tension, distance, speed along the surface, and progress to the finish. Its hierarchy lives in the scene, and `RideHud` only writes the numbers into it. It is not built from the Ladybug UI textures.
- Coins from a run are distance pay plus coins picked up. A retry does not add that payout a second time. Spending coins on upgrades persists between sessions.

## With more time

- Ride the full spline as the finish, and block the end with a readable gate instead of the 70% test line.
- Pose and scale Ladybug against the sled in the editor.
- Replace the code-built HUD with Ladybug UI.
- Add a steering-response upgrade. Launch power, sled friction, and income are the three that exist now.
- Scatter more of Frozen Paris (benches, park edges) once their size is tuned, and ship a player build.
