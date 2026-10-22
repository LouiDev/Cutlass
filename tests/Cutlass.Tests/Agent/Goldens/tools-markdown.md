# Cutlass tools

Call any tool with `POST /v1/call`, the header `Authorization: Bearer <token>` and the body `{"tool": "<name>", "args": {...}}`. After `cutlass_acquire_lock`, send its `lockId` as the header `X-Cutlass-Lock`. Every reply is the envelope `{"revision", "warnings", "result"}` or `{"revision", "error"}`. Image tools return `pngBase64` in `result`. Positions are integer frames; source times are flicks (705600000 per second) or seconds.

## cutlass_status

State of Cutlass: project, ffmpeg, who holds the edit lock. Call first.

- Catalog name `status`, route `GET /v1/status`.
- No lock needed; returns JSON.

No parameters.

Example call:

```json
{
  "tool": "cutlass_status",
  "args": {}
}
```

## cutlass_capabilities

Export presets, qualities, transition kinds, title anchors, edit modes and limits of this build and ffmpeg.

- Catalog name `capabilities`, route `GET /v1/capabilities`.
- No lock needed; returns JSON.

No parameters.

Example call:

```json
{
  "tool": "cutlass_capabilities",
  "args": {}
}
```

## cutlass_acquire_lock

Take the edit lock before any change. The user sees your name and reason.

- Catalog name `lock.acquire`, route `POST /v1/lock/acquire`.
- No lock needed; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `agentName` | string | yes | Your name as the user should see it. |
| `reason` | string | yes | What you are about to do, in a few words. |
| `waitMs` | integer, 0 to 120000 | no | Wait this long (at most 120000) when the user or another agent is busy. |
| `leaseSeconds` | integer, 15 to 600 | no | Lease length, 15 to the configured maximum; renewed by every call you make. |

Example call:

```json
{
  "tool": "cutlass_acquire_lock",
  "args": {
    "agentName": "<agentName>",
    "reason": "<reason>"
  }
}
```

## cutlass_renew_lock

Renew your lease during a long pause. Every other call renews it as well.

- Catalog name `lock.renew`, route `POST /v1/lock/renew`.
- Needs the lock; returns JSON.

No parameters.

Example call:

```json
{
  "tool": "cutlass_renew_lock",
  "args": {}
}
```

## cutlass_release_lock

Give the project back to the user. Always call when done.

- Catalog name `lock.release`, route `POST /v1/lock/release`.
- Needs the lock; returns JSON.

No parameters.

Example call:

```json
{
  "tool": "cutlass_release_lock",
  "args": {}
}
```

## cutlass_get_lock

Who holds the edit lock, its phase and lease.

- Catalog name `lock.get`, route `GET /v1/lock`.
- No lock needed; returns JSON.

No parameters.

Example call:

```json
{
  "tool": "cutlass_get_lock",
  "args": {}
}
```

## cutlass_get_project

Timeline as JSON: tracks, clips (ids, frames, source times), transitions, markers, media.

- Catalog name `project.get`, route `GET /v1/project`.
- No lock needed; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `trackIds` | array of string | no | Only these tracks. |
| `from` | integer, at least 0 | no | Only clips ending after this frame. |
| `to` | integer, at least 0 | no | Only clips starting before this frame. |

Example call:

```json
{
  "tool": "cutlass_get_project",
  "args": {}
}
```

## cutlass_describe_project

Short text overview of the timeline; cheaper than get_project.

- Catalog name `project.describe`, route `GET /v1/project/describe`.
- No lock needed; returns text (`result.text` through `/v1/call`).

| Parameter | Type | Required | Description |
|---|---|---|---|
| `from` | integer, at least 0 | no | Only clips ending after this frame. |
| `to` | integer, at least 0 | no | Only clips starting before this frame. |

Example call:

```json
{
  "tool": "cutlass_describe_project",
  "args": {}
}
```

## cutlass_new_project

Create a new untitled project (V1, V2, A1, A2).

- Catalog name `project.new`, route `POST /v1/project/new`.
- Needs the lock; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `width` | integer, 16 to 7680 | no | Canvas width (even). |
| `height` | integer, 16 to 4320 | no | Canvas height (even). |
| `frameRate` | string | no | Frame rate such as 30000/1001, 25 or 30. |
| `discardChanges` | boolean | no | Headless only: drop unsaved changes. |

Example call:

```json
{
  "tool": "cutlass_new_project",
  "args": {}
}
```

## cutlass_open_project

Open a project file inside the allowed folders.

- Catalog name `project.open`, route `POST /v1/project/open`.
- Needs the lock; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `path` | string | yes | The .cutlass file. |
| `discardChanges` | boolean | no | Headless only: drop unsaved changes. |

Example call:

```json
{
  "tool": "cutlass_open_project",
  "args": {
    "path": "<path>"
  }
}
```

## cutlass_save_project

Save the project to its current path (an untitled project needs save-as).

- Catalog name `project.save`, route `POST /v1/project/save`.
- Needs the lock; returns JSON.

No parameters.

Example call:

```json
{
  "tool": "cutlass_save_project",
  "args": {}
}
```

## cutlass_list_media

List the media in the bin with duration, codecs and online state.

- Catalog name `media.list`, route `GET /v1/media`.
- No lock needed; returns JSON.

No parameters.

Example call:

```json
{
  "tool": "cutlass_list_media",
  "args": {}
}
```

## cutlass_get_media

One media asset with probe info and online state.

- Catalog name `media.get`, route `GET /v1/media/{mediaId}`.
- No lock needed; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `mediaId` | string | yes | The media id. |

Example call:

```json
{
  "tool": "cutlass_get_media",
  "args": {
    "mediaId": "<mediaId>"
  }
}
```

## cutlass_import_media

Add files to the bin (allowed folders only); place them with place_media.

- Catalog name `media.import`, route `POST /v1/media/import`.
- Needs the lock; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `paths` | array of string, at most 32 items | yes | Absolute file paths. |

Example call:

```json
{
  "tool": "cutlass_import_media",
  "args": {
    "paths": [
      "<path>"
    ]
  }
}
```

## cutlass_remove_media

Remove a media asset and every clip that uses it.

- Catalog name `media.remove`, route `DELETE /v1/media/{mediaId}`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `mediaId` | string | yes | The media id. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_remove_media",
  "args": {
    "mediaId": "<mediaId>"
  }
}
```

## cutlass_source_levels

Loudness of a media file's own audio (RMS and peak dBFS per interval).

- Catalog name `media.levels`, route `POST /v1/media/{mediaId}/levels`.
- No lock needed; a render or analysis; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `mediaId` | string | yes | The media id. |
| `startFlicks` | integer | no | Source start in flicks. |
| `startSeconds` | number | no | Source start in seconds. |
| `endFlicks` | integer | no | Source end in flicks. |
| `endSeconds` | number | no | Source end in seconds. |
| `intervalMs` | integer, 10 to 1000 | no | Interval, 10 to 1000 (50). |

Example call:

```json
{
  "tool": "cutlass_source_levels",
  "args": {
    "mediaId": "<mediaId>"
  }
}
```

## cutlass_render_frame

Look at one timeline frame. Returns an image.

- Catalog name `frame.render`, route `GET /v1/frame/{frame}`.
- No lock needed; a render or analysis; returns an image (`pngBase64` through `/v1/call`).

| Parameter | Type | Required | Description |
|---|---|---|---|
| `frame` | integer, at least 0 | yes | Timeline frame. |
| `width` | integer, 16 to 1920 | no | Image width, 16 to 1920, even (640). |
| `useProxies` | boolean | no | Render from proxies. |
| `format` | string: png, base64 | no | png or base64. |

Example call:

```json
{
  "tool": "cutlass_render_frame",
  "args": {
    "frame": 0
  }
}
```

## cutlass_contact_sheet

Many frames tiled in one image, with a cell legend.

- Catalog name `contact-sheet`, route `POST /v1/contact-sheet`.
- No lock needed; a render or analysis; returns an image (`pngBase64` through `/v1/call`).

| Parameter | Type | Required | Description |
|---|---|---|---|
| `frames` | array of integer, at most 64 items | no | Frames to show (at most 64). |
| `start` | integer | no | First frame of an even spread. |
| `end` | integer | no | End frame of the spread. |
| `count` | integer, 1 to 64 | no | Cells of the spread (at most 64). |
| `cellWidth` | integer, 16 to 1920 | no | Cell width, 16 to 1920 (320). |
| `columns` | integer, 1 to 64 | no | Columns (default: a near square grid). |
| `format` | string: png, base64 | no | png or base64. |

Example call:

```json
{
  "tool": "cutlass_contact_sheet",
  "args": {}
}
```

## cutlass_audio_levels

Loudness (RMS and peak dBFS) of the mix per interval.

- Catalog name `audio.levels`, route `POST /v1/audio/levels`.
- No lock needed; a render or analysis; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `start` | integer, at least 0 | yes | First frame. |
| `end` | integer, at least 0 | yes | End frame (exclusive). |
| `intervalMs` | integer, 10 to 1000 | no | Interval, 10 to 1000 (50). |
| `trackIds` | array of string | no | Only these audio tracks (soloed in a snapshot). |

Example call:

```json
{
  "tool": "cutlass_audio_levels",
  "args": {
    "start": 0,
    "end": 0
  }
}
```

## cutlass_find_silences

Frame ranges quieter than the threshold.

- Catalog name `audio.silences`, route `POST /v1/audio/silences`.
- No lock needed; a render or analysis; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `start` | integer, at least 0 | no | First frame (0). |
| `end` | integer, at least 0 | no | End frame (the sequence end). |
| `thresholdDb` | number | no | Threshold in dBFS (-40). |
| `minDurationMs` | integer, at least 0 | no | Shortest silence (500). |
| `padMs` | integer, at least 0 | no | Keep this much sound on each side (100). |
| `trackIds` | array of string | no | Only these audio tracks. |

Example call:

```json
{
  "tool": "cutlass_find_silences",
  "args": {}
}
```

## cutlass_set_playhead

Show the user a frame in the viewer.

- Catalog name `playhead.set`, route `POST /v1/playhead`.
- Needs the lock; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `frame` | integer, at least 0 | yes | Timeline frame. |

Example call:

```json
{
  "tool": "cutlass_set_playhead",
  "args": {
    "frame": 0
  }
}
```

## cutlass_place_media

Put media, or a source range of it, on the timeline.

- Catalog name `edit.place`, route `POST /v1/edit/place`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `mediaId` | string | yes | The media id. |
| `at` | integer, at least 0 | yes | Timeline frame. |
| `mode` | string: overwrite, insert | no | overwrite (default) or insert. |
| `streams` | string: both, video, audio | no | both (default), video or audio. |
| `videoTrackId` | string | no | Video track (default: targeted). |
| `audioTrackId` | string | no | Audio track (default: targeted). |
| `sourceInFlicks` | integer | no | Source in point. |
| `sourceInSeconds` | number | no | Source in point in seconds. |
| `sourceOutFlicks` | integer | no | Source out point. |
| `sourceOutSeconds` | number | no | Source out point in seconds. |
| `durationFrames` | integer, at least 1 | no | Length in frames (stills, or instead of an out point). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_place_media",
  "args": {
    "mediaId": "<mediaId>",
    "at": 0
  }
}
```

## cutlass_move_clips

Move clips in time and between tracks.

- Catalog name `edit.move`, route `POST /v1/edit/move`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipIds` | array of string, at most 1000 items | yes | Clip ids. |
| `deltaFrames` | integer | yes | Frames to move (negative: left). |
| `deltaTracks` | integer | no | Tracks to move (positive: up for video). |
| `mode` | string: overwrite, insert | no | overwrite (default) or insert. |
| `includeLinked` | boolean | no | Move linked partners too (true). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_move_clips",
  "args": {
    "clipIds": [
      "<clipId>"
    ],
    "deltaFrames": 0
  }
}
```

## cutlass_trim_clip

Trim a clip's start or end, optionally rippling later clips.

- Catalog name `edit.trim`, route `POST /v1/edit/trim`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipId` | string | yes | The clip. |
| `edge` | string: start, end | yes | start or end. |
| `deltaFrames` | integer | no | Frames to move the edge. |
| `toFrame` | integer, at least 0 | no | Move the edge to this frame. |
| `ripple` | boolean | no | Shift later clips (false). |
| `includeLinked` | boolean | no | Trim linked partners too (true). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_trim_clip",
  "args": {
    "clipId": "<clipId>",
    "edge": "start"
  }
}
```

## cutlass_roll_edit

Move the cut between two butting clips.

- Catalog name `edit.roll`, route `POST /v1/edit/roll`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `leftClipId` | string | yes | The outgoing clip. |
| `rightClipId` | string | yes | The incoming clip. |
| `deltaFrames` | integer | yes | Frames to move the cut. |
| `includeLinked` | boolean | no | Roll linked partners too (true). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_roll_edit",
  "args": {
    "leftClipId": "<leftClipId>",
    "rightClipId": "<rightClipId>",
    "deltaFrames": 0
  }
}
```

## cutlass_slip_clip

Change which source part a clip shows without moving it.

- Catalog name `edit.slip`, route `POST /v1/edit/slip`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipId` | string | yes | The clip. |
| `deltaFrames` | integer | yes | Frames to slip the source. |
| `includeLinked` | boolean | no | Slip linked partners too (true). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_slip_clip",
  "args": {
    "clipId": "<clipId>",
    "deltaFrames": 0
  }
}
```

## cutlass_split

Split clips at a frame (default: every unlocked track).

- Catalog name `edit.split`, route `POST /v1/edit/split`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `frame` | integer, at least 0 | yes | Timeline frame. |
| `trackIds` | array of string | no | Only these tracks. |
| `clipIds` | array of string | no | Only these clips. |
| `includeLinked` | boolean | no | Split linked partners too (true). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_split",
  "args": {
    "frame": 0
  }
}
```

## cutlass_delete_clips

Delete clips, leaving gaps or closing them up (ripple).

- Catalog name `edit.delete`, route `POST /v1/edit/delete`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipIds` | array of string, at most 1000 items | yes | Clip ids. |
| `ripple` | boolean | no | Close the gaps (false). |
| `includeLinked` | boolean | no | Delete linked partners too (true). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_delete_clips",
  "args": {
    "clipIds": [
      "<clipId>"
    ]
  }
}
```

## cutlass_remove_ranges

Cut time ranges out of all unlocked tracks (or the given ones) and close the gaps.

- Catalog name `edit.remove-ranges`, route `POST /v1/edit/remove-ranges`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ranges` | array of object, at most 500 items | yes | Ranges [start, end) in frames (at most 500). |
| `ranges[].start` | integer, at least 0 | yes | First frame. |
| `ranges[].end` | integer, at least 0 | yes | End frame (exclusive). |
| `trackIds` | array of string | no | Only these tracks. |
| `ripple` | boolean | no | Close the gaps (true). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_remove_ranges",
  "args": {
    "ranges": [
      {
        "start": 0,
        "end": 0
      }
    ]
  }
}
```

## cutlass_close_gap

Close the gap at a frame on one track by moving later clips left.

- Catalog name `edit.close-gap`, route `POST /v1/edit/close-gap`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `trackId` | string | yes | The track. |
| `gapStart` | integer, at least 0 | yes | A frame inside or at the start of the gap. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_close_gap",
  "args": {
    "trackId": "<trackId>",
    "gapStart": 0
  }
}
```

## cutlass_set_clip_props

Opacity, transform, colour, gain, fades, enable.

- Catalog name `edit.clip-props`, route `POST /v1/edit/clip-props`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipIds` | array of string, at most 1000 items | yes | Clip ids. |
| `enabled` | boolean | no | Enable or disable. |
| `opacity` | number | no | 0 to 1 (video and titles). |
| `transform` | object | no | Video placement (fields optional). |
| `transform.x` | number | no | Offset from centre in pixels. |
| `transform.y` | number | no | Offset from centre in pixels. |
| `transform.scale` | number | no | Scale (1 fits). |
| `transform.cropLeft` | number | no | 0 to 0.9. |
| `transform.cropTop` | number | no | 0 to 0.9. |
| `transform.cropRight` | number | no | 0 to 0.9. |
| `transform.cropBottom` | number | no | 0 to 0.9. |
| `color` | object | no | Colour adjustment (fields optional). |
| `color.brightness` | number | no | -1 to 1 (0). |
| `color.contrast` | number | no | 0 to 2 (1). |
| `color.saturation` | number | no | 0 to 3 (1). |
| `color.gamma` | number | no | 0.1 to 4 (1). |
| `gainDb` | number | no | Clip gain in dB (audio). |
| `fadeInFrames` | integer, at least 0 | no | Fade-in frames. |
| `fadeOutFrames` | integer, at least 0 | no | Fade-out frames. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_set_clip_props",
  "args": {
    "clipIds": [
      "<clipId>"
    ]
  }
}
```

## cutlass_set_speed

Set clip speed in percent.

- Catalog name `edit.speed`, route `POST /v1/edit/speed`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipIds` | array of string, at most 1000 items | yes | Clip ids. |
| `percent` | integer, 25 to 400 | yes | 25 to 400. |
| `rippleFollowing` | boolean | no | Shift later clips (true). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_set_speed",
  "args": {
    "clipIds": [
      "<clipId>"
    ],
    "percent": 25
  }
}
```

## cutlass_link_clips

Link clips so they select and move together.

- Catalog name `edit.link`, route `POST /v1/edit/link`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipIds` | array of string, at most 1000 items | yes | Clip ids. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_link_clips",
  "args": {
    "clipIds": [
      "<clipId>"
    ]
  }
}
```

## cutlass_unlink_clips

Unlink clips.

- Catalog name `edit.unlink`, route `POST /v1/edit/unlink`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipIds` | array of string, at most 1000 items | yes | Clip ids. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_unlink_clips",
  "args": {
    "clipIds": [
      "<clipId>"
    ]
  }
}
```

## cutlass_set_volume_keys

Replace an audio clip's volume envelope (empty clears).

- Catalog name `edit.volume-keys`, route `POST /v1/edit/volume-keys`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipId` | string | yes | The audio clip. |
| `keys` | array of object, at most 1000 items | yes | Keys relative to the clip start. |
| `keys[].frame` | integer, at least 0 | yes | Frame from the clip start. |
| `keys[].db` | number | yes | Level in dB. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_set_volume_keys",
  "args": {
    "clipId": "<clipId>",
    "keys": [
      {
        "frame": 0,
        "db": 0
      }
    ]
  }
}
```

## cutlass_add_transition

Add a transition on the cut between two butting clips.

- Catalog name `edit.transition.add`, route `POST /v1/edit/transition/add`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `trackId` | string | yes | The track. |
| `cutFrame` | integer, at least 0 | yes | The cut frame. |
| `kind` | string: crossfade, dipToBlack, dipToWhite, wipeLeft, wipeRight, slideLeft, slideRight, circleOpen, audioCrossfade | no | Transition kind (crossfade). |
| `durationFrames` | integer, at least 2 | yes | Length in frames (at least 2). |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_add_transition",
  "args": {
    "trackId": "<trackId>",
    "cutFrame": 0,
    "durationFrames": 2
  }
}
```

## cutlass_update_transition

Change a transition's kind or length.

- Catalog name `edit.transition.update`, route `POST /v1/edit/transition/update`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `transitionId` | string | yes | The transition. |
| `kind` | string: crossfade, dipToBlack, dipToWhite, wipeLeft, wipeRight, slideLeft, slideRight, circleOpen, audioCrossfade | no | New kind. |
| `durationFrames` | integer, at least 2 | no | New length. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_update_transition",
  "args": {
    "transitionId": "<transitionId>"
  }
}
```

## cutlass_remove_transitions

Remove transitions.

- Catalog name `edit.transition.remove`, route `POST /v1/edit/transition/remove`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `transitionIds` | array of string, at most 1000 items | yes | Transition ids. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_remove_transitions",
  "args": {
    "transitionIds": [
      "<transitionId>"
    ]
  }
}
```

## cutlass_add_title

Add a text title on a video track.

- Catalog name `edit.title.add`, route `POST /v1/edit/title/add`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `trackId` | string | yes | A video track. |
| `at` | integer, at least 0 | yes | Timeline frame. |
| `durationFrames` | integer, at least 1 | yes | Length in frames. |
| `mode` | string: overwrite, insert | no | overwrite (default) or insert. |
| `title` | object | yes | Text and style (only text is required). |
| `title.text` | string | yes | The text (lines separated by \n). |
| `title.font` | string | no | builtin:Inter-Bold, a system family or a font file. |
| `title.size` | number | no | Font size in canvas pixels. |
| `title.color` | string | no | #RRGGBB or #RRGGBBAA. |
| `title.outlineWidth` | number | no | Outline width (0: none). |
| `title.outlineColor` | string | no | #RRGGBB or #RRGGBBAA. |
| `title.box` | boolean | no | Background box. |
| `title.boxColor` | string | no | #RRGGBB or #RRGGBBAA. |
| `title.boxPadding` | number | no | Box padding in pixels. |
| `title.anchor` | string: center, lowerThird, top | no | Position preset. |
| `title.offsetX` | number | no | Offset from the anchor in pixels. |
| `title.offsetY` | number | no | Offset from the anchor in pixels. |
| `opacity` | number | no | 0 to 1. |
| `fadeInFrames` | integer, at least 0 | no | Fade-in frames. |
| `fadeOutFrames` | integer, at least 0 | no | Fade-out frames. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_add_title",
  "args": {
    "trackId": "<trackId>",
    "at": 0,
    "durationFrames": 1,
    "title": {
      "text": "<text>"
    }
  }
}
```

## cutlass_update_title

Change a title's text, style, opacity or fades.

- Catalog name `edit.title.update`, route `POST /v1/edit/title/update`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `clipId` | string | yes | The title clip. |
| `title` | object | no | Fields to change. |
| `title.text` | string | no | The text (lines separated by \n). |
| `title.font` | string | no | builtin:Inter-Bold, a system family or a font file. |
| `title.size` | number | no | Font size in canvas pixels. |
| `title.color` | string | no | #RRGGBB or #RRGGBBAA. |
| `title.outlineWidth` | number | no | Outline width (0: none). |
| `title.outlineColor` | string | no | #RRGGBB or #RRGGBBAA. |
| `title.box` | boolean | no | Background box. |
| `title.boxColor` | string | no | #RRGGBB or #RRGGBBAA. |
| `title.boxPadding` | number | no | Box padding in pixels. |
| `title.anchor` | string: center, lowerThird, top | no | Position preset. |
| `title.offsetX` | number | no | Offset from the anchor in pixels. |
| `title.offsetY` | number | no | Offset from the anchor in pixels. |
| `opacity` | number | no | 0 to 1. |
| `fadeInFrames` | integer, at least 0 | no | Fade-in frames. |
| `fadeOutFrames` | integer, at least 0 | no | Fade-out frames. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_update_title",
  "args": {
    "clipId": "<clipId>"
  }
}
```

## cutlass_add_marker

Add a marker.

- Catalog name `edit.marker.add`, route `POST /v1/edit/marker/add`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `frame` | integer, at least 0 | yes | Timeline frame. |
| `name` | string | no | Label. |
| `color` | string | no | #RRGGBB. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_add_marker",
  "args": {
    "frame": 0
  }
}
```

## cutlass_update_marker

Move or rename a marker.

- Catalog name `edit.marker.update`, route `POST /v1/edit/marker/update`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `markerId` | string | yes | The marker. |
| `frame` | integer, at least 0 | no | New frame. |
| `name` | string | no | New label. |
| `color` | string | no | #RRGGBB. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_update_marker",
  "args": {
    "markerId": "<markerId>"
  }
}
```

## cutlass_remove_markers

Remove markers.

- Catalog name `edit.marker.remove`, route `POST /v1/edit/marker/remove`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `markerIds` | array of string, at most 1000 items | yes | Marker ids. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_remove_markers",
  "args": {
    "markerIds": [
      "<markerId>"
    ]
  }
}
```

## cutlass_set_in_out

Set or clear the In and Out points (null clears).

- Catalog name `edit.in-out`, route `POST /v1/edit/in-out`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `inFrame` | integer or null | no | In frame or null. |
| `outFrame` | integer or null | no | Out frame (exclusive) or null. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_set_in_out",
  "args": {}
}
```

## cutlass_add_track

Add a video or audio track.

- Catalog name `edit.track.add`, route `POST /v1/edit/track/add`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `kind` | string: video, audio | yes | video or audio. |
| `index` | integer, at least 1 | no | Kind index (1 is V1 or A1); default: on top or bottom. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_add_track",
  "args": {
    "kind": "video"
  }
}
```

## cutlass_update_track

Track name, hidden, locked, targeted, mute, solo and gain.

- Catalog name `edit.track.update`, route `POST /v1/edit/track/update`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `trackId` | string | yes | The track. |
| `name` | string | no | New name. |
| `hidden` | boolean | no | Video: hide. |
| `locked` | boolean | no | Lock against edits. |
| `targeted` | boolean | no | Target for placing. |
| `muted` | boolean | no | Audio: mute. |
| `solo` | boolean | no | Audio: solo. |
| `gainDb` | number | no | Audio: track gain in dB. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_update_track",
  "args": {
    "trackId": "<trackId>"
  }
}
```

## cutlass_remove_track

Remove a track and its clips.

- Catalog name `edit.track.remove`, route `POST /v1/edit/track/remove`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `trackId` | string | yes | The track. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_remove_track",
  "args": {
    "trackId": "<trackId>"
  }
}
```

## cutlass_set_sequence

Sequence size and frame rate.

- Catalog name `edit.sequence`, route `POST /v1/edit/sequence`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `width` | integer, 16 to 7680 | no | Canvas width (even). |
| `height` | integer, 16 to 4320 | no | Canvas height (even). |
| `frameRate` | string | no | Frame rate such as 30000/1001. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_set_sequence",
  "args": {}
}
```

## cutlass_undo

Undo your own last step, never the user's.

- Catalog name `undo`, route `POST /v1/undo`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_undo",
  "args": {}
}
```

## cutlass_redo

Redo your own last undone step.

- Catalog name `redo`, route `POST /v1/redo`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_redo",
  "args": {}
}
```

## cutlass_begin_batch

Group edits into one undo step "Agent: label".

- Catalog name `batch.begin`, route `POST /v1/batch/begin`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Short and human, for example Remove silences. |
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_begin_batch",
  "args": {
    "label": "<label>"
  }
}
```

## cutlass_end_batch

Close the batch as one undo step.

- Catalog name `batch.end`, route `POST /v1/batch/end`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_end_batch",
  "args": {}
}
```

## cutlass_cancel_batch

Revert every edit since begin_batch and close the batch.

- Catalog name `batch.cancel`, route `POST /v1/batch/cancel`.
- Needs the lock; changes the project (one undo step); returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ifRevision` | integer | no | Fail with conflict when the project revision is no longer this. |
| `returnProject` | boolean | no | Also return the project summary. |

Example call:

```json
{
  "tool": "cutlass_cancel_batch",
  "args": {}
}
```

## cutlass_start_export

Render to a file inside the write folders; follow it with export_status.

- Catalog name `exports.start`, route `POST /v1/exports`.
- Needs the lock; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `preset` | string | yes | Preset id such as h264 (see capabilities). |
| `quality` | string: draft, good, high, best | no | draft, good, high or best (high). |
| `height` | integer, at least 16 | no | Output height. |
| `width` | integer, at least 16 | no | GIF width. |
| `range` | string | no | whole (default) or inOut; use start and end for a frame range. |
| `start` | integer, at least 0 | no | First frame of a custom range. |
| `end` | integer, at least 0 | no | End frame of a custom range. |
| `outputPath` | string | yes | The output file. |
| `limitPeaks` | boolean | no | Peak limiter (true). |
| `overwrite` | boolean | no | Replace an existing file (false). |

Example call:

```json
{
  "tool": "cutlass_start_export",
  "args": {
    "preset": "<preset>",
    "outputPath": "<outputPath>"
  }
}
```

## cutlass_list_exports

This process's exports, oldest first.

- Catalog name `exports.list`, route `GET /v1/exports`.
- No lock needed; returns JSON.

No parameters.

Example call:

```json
{
  "tool": "cutlass_list_exports",
  "args": {}
}
```

## cutlass_export_status

An export's state and progress; waits up to waitMs for a change.

- Catalog name `exports.get`, route `GET /v1/exports/{exportId}`.
- No lock needed; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `exportId` | string | yes | The export id. |
| `waitMs` | integer, 0 to 25000 | no | Wait at most this long for a change (25000 max). |

Example call:

```json
{
  "tool": "cutlass_export_status",
  "args": {
    "exportId": "<exportId>"
  }
}
```

## cutlass_cancel_export

Cancel an export gracefully.

- Catalog name `exports.cancel`, route `POST /v1/exports/{exportId}/cancel`.
- Needs the lock; returns JSON.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `exportId` | string | yes | The export id. |

Example call:

```json
{
  "tool": "cutlass_cancel_export",
  "args": {
    "exportId": "<exportId>"
  }
}
```
