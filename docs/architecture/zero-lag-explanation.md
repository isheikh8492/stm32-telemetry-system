Beginner-friendly walkthrough of each layer. I'll explain the concept first, then how we applied it.

1. Producer / Consumer
   The concept: One job reads input as fast as it arrives. A separate job processes that input. Between them sits a thread-safe queue. Each side runs independently — the reader doesn't slow down because the processor is busy, and vice versa.

Analogy: kitchen with a chef and a waiter. The chef plates food, puts it on a counter (queue), the waiter picks it up. If they shared one person, you'd have nobody plating while food is being served.

How we did it:

SerialReader runs on its own thread, just reading bytes off the COM port and decoding them into Event objects. That's all it does.
SerialProducer wraps it and pushes each Event into a System.Threading.Channels.Channel<Event> (a built-in thread-safe queue).
BufferConsumer runs on a different thread, pulling Events out of that queue and appending them to the buffer.
Key tuning knobs:

The queue is bounded at 1024 (won't grow forever) and uses DropOldest (if the consumer falls behind, drop old frames rather than memory-bomb).
SingleReader = true, SingleWriter = true tells the queue "there's only ever one of each side", which lets it skip locks.
Beginner lesson: I/O work and CPU work belong on different threads. Mixing them serializes everything to the slowest part.

2. Buffer
   The concept: "Row store vs column store." If you have a million rows each with 10 columns, and you only ever query one column, it's faster to lay out the data column-by-column.

Same idea here. We get events shaped like:

Event { id, channels: [
Channel { id: 0, params: { baseline, area, peakWidth, peakHeight } },
Channel { id: 1, params: { ... } },
...
]}
But every histogram is a query like "give me Channel 5's PeakHeight over the last 10,000 events." Storing events as-is, that query has to loop through 10,000 events, reach into each one, find Channel 5, pull out PeakHeight. Slow.

How we did it:

We laid the data out as 240 separate arrays of doubles, one per (channel × param) combination. Each array is 10,000 long.
When an event arrives, the producer extracts each (channelId, paramType) value and writes it directly into the right array.
Now "Channel 5's PeakHeight" is just \_featureRings[5*4 + (int)PeakHeight] — a contiguous array, no loops, no lookups.
The subtle gotcha: the firmware sends channels in any order. Originally I used evt.Channels[i].ChannelId for the channel id... wait no, I used i (the position in the list). That's a bug — channel 5 might be in slot 3 of the list. Fixed by always indexing on ChannelId.

Beginner lesson: Lay out your data the way you read it, not the way it arrives.

3. Processing engine
   The concept: A scheduler. Doesn't compute anything itself. Decides: "which plot needs to do work right now, and which can be skipped?"

Skipping work cheaply is more valuable than doing work fast.

How we did it:

A Timer ticks every 20 ms. On each tick:
Loop through every plot.
For each, ask: Did anything actually change since the last time I processed this? (We track a fingerprint = (settings_version, latest_event_id, pixel_width, pixel_height). If it matches the last fingerprint, skip.)
Ask: Is this plot type due for a tick? (Oscilloscope every 20 ms; histograms every 250 ms.) If not, skip.
Otherwise, hand the work to the per-plot processor (next layer).
Beginner lesson: The fastest computation is one you don't do. A skip-check is cheap; the work is expensive. Spend effort on the skip-check.

4. Per-plot processors (the headline algorithm)
   The concept: Incremental state. If your answer can be updated by knowing only what's new and what just left, you don't have to recompute from scratch every time.

Analogy: counting people in a room. Naive way — every minute, count everyone present. Better — keep a tally, add 1 when someone enters, subtract 1 when someone leaves. Same answer, way less work.

How we did it (histogram):

Each histogram processor keeps two arrays:
Counts[256] — running count for each bin.
RingBins[10_000] — for each event in the trailing window, which bin it landed in.
When a new event arrives:
Compute its bin → push onto RingBins → increment Counts[bin].
If RingBins is full, evict the head → decrement Counts[old_bin].
That's it. Per tick we touch maybe 10 events instead of 10,000.

// Conceptual
void OnEvent(double value) {
int newBin = ComputeBin(value);
if (ring.IsFull) Counts[ring.Head]--; // evict
ring.Push(newBin);
Counts[newBin]++; // add
}
Subtle bit: if the buffer wraps and we miss events (we fell behind), we need to detect that and rebuild from scratch. We track LastSequence — the sequence number of the last event we saw. If the buffer's StartSequence is past our LastSequence, we've lost data and need a full rebuild.

Beginner lesson: Look at your computation and ask "do I need the whole input, or just what changed?" Most of the time, just-what-changed is enough.

5. Rendering engine
   The concept: Bridge between worker threads (which compute) and the UI thread (which paints). UI frameworks are extremely strict — only the UI thread can touch UI controls.

Two problems to solve:

How do worker threads tell the UI thread "go paint this"?
How do we make sure the UI thread doesn't get behind?
How we did it:

Problem 1 — Dispatcher.BeginInvoke(DispatcherPriority.Render, action). This is WPF's "run this on the UI thread later". The Render priority is the magic part — too low and user input elbows it out (laggy plots), too high and you starve user input. Render is the goldilocks zone.

Problem 2 — coalesce. Worker threads might produce 3 frames for the same plot before the UI thread picks any up. We don't want a queue of 3 frames; we only want the latest. Solution:

A Dictionary<plotId, latest_frame>. New frames overwrite old ones for the same plot.
A single in-flight flag (Interlocked.Exchange) ensures only one UI dispatch is queued at a time. After that dispatch finishes, it checks for new arrivals and re-queues itself if needed.
The crash that taught us about thread affinity: I wrote target.Settings.Type from the worker thread to decide rate gating. Settings is a DependencyObject — a WPF type that's bound to its creating thread. Reading it from another thread crashes. Fix: cache PlotType in the engine's data structures at registration time (which runs on the UI thread).

Beginner lesson: In UI frameworks, only the UI thread touches UI things. And: coalesce instead of queueing whenever possible.

6. UI blit (DynamicBitmap)
   The concept: When you paint a bitmap onto the screen every tick, there are two ways to do it:

Bad: Replace the Image.Source with a new bitmap. WPF then has to lay out the visual tree, ask the compositor to re-rasterize, and so on. Tens of milliseconds.
Good: Keep the same bitmap object. Each tick, copy fresh pixels into its existing back buffer (WritePixels). The compositor just sees "the same visual changed" and re-uploads to GPU. Microseconds.
Analogy: a photo frame on the wall. Bad way — every minute, take down the frame, build a new one with a new photo, hang it back up. Good way — keep the frame on the wall, slide a new photo into it.

How we did it:

DynamicBitmap extends Image, owns a WriteableBitmap of the right pixel size.
Each tick the worker thread paints into a byte[width * height * 4]. Format: 4 bytes per pixel (B, G, R, A premultiplied).
The UI thread calls \_bitmap.WritePixels(rect, buffer, stride, 0) — a memcpy.
We only allocate a new WriteableBitmap when the size changes.
Beginner lesson: Don't recreate things every frame. Reuse and overwrite.

7. ScottPlot integration
   The concept: Use a real charting library for the parts that change rarely (axes, labels, gridlines, titles) but paint your own bitmap for the parts that change every tick (the bars, the trace, the heatmap pixels).

Why? Real charting libraries do beautiful tick math, but they're expensive — calling Plot.Refresh() is 5-15 ms because it relayouts everything. We do that ~once when settings change, not every tick.

How we did it:

ScottPlot's WpfPlot provides the chrome.
We override its DataBackground to white and overlay our DynamicBitmap on top of the chrome's data area.
Every tick we just blit pixels into our bitmap. ScottPlot doesn't get involved.
OnApplySettings (called on settings change, like axis range edit) triggers a single Plot.Refresh().
For histograms, we also call Refresh if the Y-axis ceiling needs to grow — but we use NiceMax to round to 1/2/5×10ⁿ multiples, so the ceiling stays stable across small count changes.
Beginner lesson: Distinguish "rare/expensive" from "frequent/cheap" work and route them through different paths.

8. Plot lifecycle
   The concept: When a UI element is added, resized, or removed, things have to happen in the right order. WPF fires events in a particular sequence; if you wire things in the wrong order or forget to unwire on teardown, you get races, leaks, or zombie objects.

How we did it:

On Loaded (a plot just appeared on the worksheet):

Resolve the type-specific PlotItem from the registry.
Set its DataContext.
Subscribe to its DataAreaChanged event (so the bitmap layer follows the chrome's data rect).
Wire DragHandler + ThumbManager (mouse interaction).
Register with the active session so the engines know about it.
On Unloaded — undo each of those, in reverse.

Subtle one — AlignToGrid: when the user clicks empty canvas to drop a new plot, the cursor lands on a grid intersection. But the plot's data rect is offset from its host by ~30 px of axis chrome. So the data isn't centered where they clicked. Fix: for the first ~6 layout passes, adjust the host's X/Y/W/H so the data rect's TL lands on the grid intersection. Self-unsubscribes once chrome stabilizes.

Beginner lesson: Lifecycle code is where leaks live. Pair every subscribe with an unsubscribe in the matching teardown event.

9. Threading discipline
   The concept: Multi-threaded code is correct only if you have strict rules about which thread can touch what. We have three rules:

UI controls (DependencyObjects) — UI thread only. Reading a Settings.Type from a worker thread will throw.
Mutable shared data — locked, or designed for concurrent access. ChannelDataBuffer locks around every Append; reads of the underlying double[] are intentionally lock-free with eventual consistency.
Cross-thread events — explicitly marshal. When the SerialReader fires an error event from its thread, we wrap it in \_uiContext.Post(...) so subscribers see it on the UI thread.
How we enforced it:

Workers cache anything they need at registration time, on the UI thread (e.g., PlotType in RenderTargetEntry). They never read DependencyObjects later.
Snapshot types like ChannelWindowSnapshot are readonly record struct — passed by value across threads, no shared heap object.
ScottPlot's RenderFinished event fires on its own thread; we marshal it to the UI thread with Plot.Dispatcher.Invoke.
Beginner lesson: Thread bugs are silent until they aren't. Write down which thread owns each object and stick to it. WPF specifically: workers don't touch UI types, period.

10. "Style" choices forced by performance
    These look like aesthetic decisions but each one is actually load-bearing.

Choice Why it's actually performance
Pbgra32 pixel format (premultiplied alpha) WPF's native format. No conversion when blitting; no per-pixel alpha multiply during compositing.
uint packed colors instead of Color struct Packed 4-byte value, written as one int to memory. Struct version would mean 4 separate field reads in the inner loop.
Settings.Version as a monotonic int Cheapest possible "did anything change?" check. One integer compare invalidates fingerprints. No equality method, no allocation.
AxisFactory.For(scale) returning a strategy One virtual call at the start of the tick → hot loop is monomorphic (same type every iteration). Without this, every per-bin call would be a virtual dispatch.
double (not float) everywhere One memory format throughout the pipeline. No float→double conversions when ScottPlot needs doubles. The 2× memory cost is invisible compared to the avoided conversions.
Channel-major sample layout in the wire format Mirrors how the firmware composes the frame and how the host parses it. Single Buffer.BlockCopy per channel; no scatter/gather.
Beginner lesson: In performance-critical code, the "easy" choice is often the slow one. Each of these looks slightly weird; each pays for that weirdness in saved cycles.

How they interact (the meta-lesson)
These layers are not independent. They reinforce each other:

The buffer stores doubles → the processors can read doubles directly → the inner loops stay tight → the per-tick cost is small enough that rate gating (4 Hz analytics) does most of the heavy lifting.
The rendering engine dispatches at Render priority → the UI thread stays responsive → user interactions stay smooth → the plot lifecycle can fire events without competing with paint.
Threading discipline keeps DependencyObject reads on the UI thread → the engines can run on workers without crashing → the workers can run hot loops without UI thread interference.
If you remove any one of these, the others can't compensate. That's why the project documents 70+ gaps — none of them alone would give you zero-lag.

The single most important meta-lesson for a beginner: performance is a system property, not a feature you bolt on at the end. You build it in by making correct decisions at every layer simultaneously.
