#### TimeGrapher Equations

##### Part I. Instantaneous Rate Eror Graphing

Purpose. This document explains how to plot the instantaneous timing error of a watch beat sequence immediately from measured beat timestamps. It also shows how that error becomes the familiar sloped timegrapher trace.

###### 1. The core formula

For any given beat n, the instantaneous error is:

En= Tmeasured− (Tstart+ n× Itarget)

- ● Tmeasured: the exact timestamp, from the quartz reference or sample clock, when the microphone detects the “tic” or “toc”.
- ● Tstart: the timestamp of the very first beat in the sequence.
- ● n: the beat number (0, 1, 2, 3, …).
- ● Itarget: the ideal interval between beats based on the movement. Example: 125 ms for a 28,800 bph watch.


Interpretation:the formula compares the beat you actually measured against the beat time that should have occurred if the watch were perfectly on rate.

![image 1](<TimeGrapher-Equations_v0.docx_images/imageFile1.png>)

Figure 1. The instantaneous error is the horizontal offset between the ideal beat time and the actual detected beat time.

###### 2. How this becomes a ploted trace

To determine the Y-coordinate on the screen, the machine applies a display scaling or wrapping operation to the error value. Because En is cumulative, the software usually removes whole-display-height drift so the dots stay visible on screen. This is often described as a modulo wrap.

Y = En(mod Plot Height)

In plain language:the timegrapher is not plotting audio amplitude. It is plotting timing error. Each new beat lands slightly above or below the previous one depending on whether the watch is gaining or losing time.

###### 3. Why the trace makes a slope

If the watch is fast, each measured beat arrives slightly early relative to the ideal schedule. That makes En become progressively more negative or more positive, depending on sign convention. Because the error changes by nearly the same amount every beat, the plotted points line up into an angled line.

Key idea:a straight sloped trace means approximately constant rate error. A flat trace means the watch is very close to the target interval. A scattered or thick trace means noise, poor detection, or beat-to-beat instability.

###### 4. The role of T  ₐᵣ

T  ₐᵣ  is the timestamp of the very first beat recorded when the measurement cycle begins. It acts as the zero point, or anchor, for the entire calculation.

- ● Baseline: without T  ₐᵣ  the machine would not know where the beat sequence began.
- ● Resetting: each time the measurement restarts or the screen is cleared, a new T  ₐᵣ  is captured.
- ● Synchronization: once T  ₐᵣ  is known, the machine can predict when beat 1, beat 2, beat 1000, and so on should occur by repeatedly adding I ₐᵣgₑ .


###### 5. Worked example

Assume:

- ● T  ₐᵣ  = 12:00:00.000
- ● I ₐᵣgₑ  = 125 ms The ideal timestamps are then:


Beat Ideal timestamp Meaning

- Beat 0

Tstart + (0 × 125 ms) = 12:00:00.000

Anchor beat

- Beat 1


Tstart + (1 × 125 ms) = 12:00:00.125

One ideal interval later

Beat n Tstart + (n × 125 ms) General case

Any deviation of a measured beat from these ideal timestamps is the instantaneous error that gets plotted on the Y-axis.

###### 6. Practical method to graph rate eror instantly

Once you have beat timestamps, the graph can be generated immediately with a simple running calculation:

- 1. Capture the first valid beat and store it as T  ₐᵣ .
- 2. Choose I ₐᵣgₑ  from the known or estimated beat rate. Example: I ₐᵣgₑ  = 3600 / BPH seconds per beat.
- 3. For each new detected beat n, measure T ₑₐ ᵤᵣₑd.
- 4. Compute E  = T ₑₐ ᵤᵣₑd − (T  ₐᵣ  + n × I ₐᵣgₑ ).
- 5. Convert En to a screen Y position by scaling or modulo wrapping.
- 6. Plot the point at the next X position and repeat for every beat.

Important implementation note:this method graphs cumulative timing error directly from timestamps. It does not require averaging first, although averaging or smoothing can be layered on top if you want a steadier display.

- 7. Minimal pseudocode

if first_beat: Tstart = Tmeasured n = 0

ideal_time = Tstart + n * Itarget En = Tmeasured - ideal_time Y = wrap_or_scale(En) plot_point(x_index=n, y=Y) n += 1

- 8. Su mary


In one sentence: to graph rate error instantly, anchor the sequence with T  ₐᵣ , predict where each beat should occur using I ₐᵣgₑ , subtract that ideal time from the measured beat time, and plot the resulting cumulative error.

Source basis:This document was reconstructed from the provided screenshot content, without embedding the original images.

##### Part I. Calculating Rate Eror from Alternating A Events

This section restores the same-phase tic/tac formulation. If your detected events alternate as A₀, A₁, A₂, A₃, A₄, … then one phase is A₀, A₂, A₄, … and the other is A₁, A₃, A₅, … . Timegraphers commonly compute a separate rate from each phase and then average them.

###### 1. Alternating event model

A₀, A₁, A₂, A₃, A₄, … tic phase: A₀, A₂, A₄, … tac phase: A₁, A₃, A₅, …

![image 2](<TimeGrapher-Equations_v0.docx_images/imageFile2.png>)

Interpretation. The adjacent spacing is the half-beat target I_target, but the displayed rate is usually computed from the same-phase periods T_tic and T_tac. In other words: measure tic-to-tic and tac-to-tac, compare each with the nominal same-phase period, then average the two converted rates. Indexing shorthand

A₂k = A₀, A₂, A₄, … A₂k+1 = A₁, A₃, A₅, … Here k is just a counter: k = 0, 1, 2, … . So A₂k picks the even-indexed events and A₂k+1 picks the odd-indexed events. T_nom,same-phase is the nominal time from one event in a phase to the next event in that same phase. For a 28,800 bph watch, T_nom,same-phase = 7200 / 28800 = 0.250 s

= 250 ms.

###### 2. Same-phase periods For A-only timing events, the same-phase periods are:

![image 3](<TimeGrapher-Equations_v0.docx_images/imageFile3.png>)

Here k is just a counter: k = 0, 1, 2, … . So A₂k picks the even-indexed events and A₂k+1 picks the odd-indexed events. T_nom,same-phase is the nominal time from one event in a phase to the next event in that same phase. For a 28,800 bph watch, T_nom,same-phase = 7200 / 28800 = 0.250 s

= 250 ms. If the detected A events occur at sample indices n₀, n₁, n₂, … in a stream sampled at f  hertz, then:

![image 4](<TimeGrapher-Equations_v0.docx_images/imageFile4.png>)

![image 5](<TimeGrapher-Equations_v0.docx_images/imageFile5.png>)

Why do it this way? Because tic and tac are not perfectly symmetric when beat error exists. Separating the two same-phase periods reduces contamination from that asymmetry before the final displayed rate is formed.

![image 6](<TimeGrapher-Equations_v0.docx_images/imageFile6.png>)

###### 4. Implementation from sample indices

If the detected A events occur at sample indices n₀, n₁, n₂, … in a stream sampled at f  hertz, then:

![image 7](<TimeGrapher-Equations_v0.docx_images/imageFile7.png>)

The plotted line and the numerical rate are the same phenomenon described two different ways. Using the half-beat target I_target = 3600 / BPH, the cumulative timing-error form is:

![image 8](<TimeGrapher-Equations_v0.docx_images/imageFile8.png>)

These longer-window forms are usually preferable in live software because they suppress one-sample jitter and make the displayed rate steadier.

###### 5. Conection to the ploted slope

The plotted line and the numerical rate are the same phenomenon described two different ways. Using the half-beat target I_target = 3600 / BPH, the cumulative timing-error form is:

![image 9](<TimeGrapher-Equations_v0.docx_images/imageFile9.png>)

For an A-only stream, the same-phase nominal period is 2 I_target = 7200 / BPH. The slope estimate and the same-phase tic/tac average therefore converge to the same displayed seconds-per-day value.

###### 6. Worked example: 28,80 bph At 28,800 bph, the half-beat target is 125 ms, so the nominal same-phase period is 250 ms.

|Step|Result|
|---|---|
|Half-beat nominal|I_target = 3600 / 28800 = 0.125000 s = 125.000 ms|
|Same-phase nominal|T_nom,same-phase = 7200 / 28800 = 0.250000 s<br><br>= 250.000 ms|
|Measured periods|T_tic = 249.980 ms, T_tac = 249.970 ms|
|Converted rates|rate_tic = +6.912 s/day, rate_tac = +10.368 s/day|
| |Rate = (6.912 + 10.368) / 2 = +8.640 s/day|


Displayedrate

Botom line.For live A-only timing, compute same-phase tic and tac periods, convert each phase to seconds per day, then average them. That is the cleaner watchmaking presentation, and it is the part that should not have been dropped from the earlier revision.

## Part I. Beat Eror from Alternating A Events

Beat error describes asymmetry between the two successive half-beat intervals of the balance oscillation. It is not the same quantity as rate. A watch can show a reasonable seconds-per-day value and still have measurable beat error if one half-cycle takes longer than the other.

###### 1. Half-beat asy metry model

Using the same alternating A-event stream A₀, A₁, A₂, A₃, … , compare one half-beat interval with the very next half-beat interval. If the watch is perfectly symmetric, those two neighboring half-beat intervals are equal. Any difference between them is beat error.

Plain-English reading of the sequence. For the first local estimate, use A₀, A₁, and A₂. For the next local estimate, use A₂, A₃, and A₄. Then continue A₄, A₅, and A₆, and so on.

![image 10](<TimeGrapher-Equations_v0.docx_images/imageFile10.png>)

![image 11](<TimeGrapher-Equations_v0.docx_images/imageFile11.png>)

###### 2. Core beat-eror equation

For one concrete local example, take three successive A events: A₀, A₁, and A₂. Define the first half-beat as the interval from A₀ to A₁, and the second half-beat as the interval from A₁ to A₂:

- t1 = A₁ − A₀
- t2 = A₂ − A₁


The signed beat error is then:

BE₀ = (t1 − t2) / 2

For the next local estimate, shift forward by one full beat and repeat the same pattern with A₂, A₃, and A₄:

BE₁ = ((A₃ − A₂) − (A₄ − A₃)) / 2 Many timing machines display the magnitude only, so the reported beat error is often |t1 − t2| / 2 expressed in milliseconds. In plain English: compare one half-beat with the next half-beat, then divide their difference by two. Zero beat error means the two neighboring half-beat intervals are equal.

###### 3. Implementation from sample indices

If the detected A events arrive at sample indices n₀, n₁, n₂, n₃, n₄, … in a stream sampled at f  hertz, the same calculation becomes:

- t1 = (n₁ − n₀) / f
- t2 = (n₂ − n₁) / f


BE₀ = ((n₁ − n₀) − (n₂ − n₁)) / (2 f ) For the next local sample-index estimate, repeat with n₂, n₃, and n₄: BE₁ = ((n₃ − n₂) − (n₄ − n₃)) / (2 f ). In live software, it is common to average a short run of these local estimates so the displayed beat error is steadier.

Connection to the rate section: the same-phase periods used for rate are T_tic = A₂ − A₀ and T_tac = A₃ − A₁. Beat error looks inside those same-phase periods and asks whether the middle event lands exactly halfway.

###### 4. Worked example: 28,80 bph

At 28,800 bph, the nominal half-beat interval is I_target = 3600 / 28800 = 0.125 s = 125.0 ms. Suppose one local measurement gives t1 = 125.8 ms and t2 = 124.2 ms.

### BE= (125.8 ms − 124.2 ms) / 2 = 0.8 ms

So the displayed beat error would be 0.8 ms in magnitude. The watch may still have an acceptable average rate, but the two half-cycles are not balanced equally around the center.

Bottom line. Rate answers “how fast or slow is the watch overall?” Beat error answers “are the two halves of the oscillation symmetric?” A complete timing display usually needs both.

# Part IV. Calculating Amplitude from A and C Events

Amplitude estimates the angular swing of the balance wheel. In a timing-machine style calculation, it is inferred from the watch beat rate, the lift angle, and the measured time between the A and C landmarks of the same beat packet.

###### 1. A-to-C interval model

For one beat packet, let A  be the selected A timestamp and C  be the matching C timestamp. The measurement used for amplitude is the elapsed time from A to C. The reference-style diagram below shows the same A–B–C labeling convention that many timing-machine explanations use.

![image 12](<TimeGrapher-Equations_v0.docx_images/imageFile12.png>)

![image 13](<TimeGrapher-Equations_v0.docx_images/imageFile13.png>)

![image 14](<TimeGrapher-Equations_v0.docx_images/imageFile14.png>)

![image 15](<TimeGrapher-Equations_v0.docx_images/imageFile15.png>)

t_AC = C  − A

This A-to-C interval must be measured in seconds and must come from the same beat packet. In other words, do not pair the A from one beat with the C from the next beat.

###### 2. Core amplitude equation

Using the standard timing-machine formula shown in your reference, the computed balance amplitude is:

![image 16](<TimeGrapher-Equations_v0.docx_images/imageFile16.png>)

##### π – 3.14

###### Terms used in the formula:

Term Meaning Units

Amp Amplitude of the balance wheel swing degrees

λ Lift angle configured for the movement degrees

n Beat rate of the watch (BPH) beats/hour

t_AC Time from A to C for one beat packet seconds

###### A practical rule of thumb is that amplitude is inversely proportional to the measured A-to-C time. A smaller A-to-C interval produces a larger amplitude estimate, and a larger A-to-C interval produces a smaller one.

Important: lift angle should be treated as a user-configurable movement parameter. 52° is common, but it is not universal.

###### 3. Implementation from detected timestamps

###### If the detector returns A and C timestamps directly, compute the interval first and then substitute it into the amplitude equation. If the detector returns sample indices instead, convert the index difference into seconds using the sample rate f .

t_AC = C  − A  t_AC = (c  − a ) / f

Amp  = (3600 λ f ) / (π n (c  − a ))

This makes the sensitivity clear: small timestamp errors in A or C can noticeably move the amplitude result, because the computation divides by the measured A-to-C interval.

###### 4. Worked example: 28,80 bph

###### Using the example values from your reference image:

C Ampalcu=la(3600tion× 52) / (π × 28,800 × 0.009) ≈ 187,200 / 814.30 ≈ 230.0°

Inputs Lift angle λ = 52° Beat rate n = 28,800 bph A-to-C time t_AC = 0.009 s

Amplitude ≈ 230°

In plain English: if the watch has a 52° lift angle and the measured A-to-C time is about

- 9 ms at 28,800 bph, the standard formula yields an amplitude of about 230 degrees.


Bottom line. Rate tells you how fast or slow the watch runs overall. Beat error tells you how symmetric the oscillation is. Amplitude tells you how far the balance wheel is swinging. A complete timing display usually wants all three.
