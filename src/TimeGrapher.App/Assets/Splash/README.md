# Splash frames

`splash_0001.png` … `splash_0074.png` (640x360) are the frames the app actually
ships; `TimeGrapher.App.csproj` embeds only `Assets\Splash\*.png` as Avalonia
resources. The app plays them at 30 fps, so the splash runs ~2.47 s (74 / 30).

`Source/intro.mp4` is the original intro (1280x720, 24 fps, 6 s). The shipped
frames come from a 2x-speed version of it — the whole intro plays start to finish
in half the time — downscaled to 640x360:

```
# 1. speed the original up 2x (6 s -> ~3 s, still 24 fps)
ffmpeg -y -i Source/intro.mp4 -filter:v "setpts=0.5*PTS" -an Source/intro_2x.mp4

# 2. extract every frame at 640x360 (~74 frames)
ffmpeg -y -i Source/intro_2x.mp4 -vf "scale=640:360:flags=lanczos" -an splash_%04d.png
```

Neither mp4 is embedded in the application — they are kept only so the frames can
be regenerated.
