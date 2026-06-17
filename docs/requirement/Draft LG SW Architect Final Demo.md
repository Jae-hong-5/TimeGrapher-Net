| | | |
|---|---|---|
|Area|Title and Scoring Criteria|Points Possible|
| | | |
|1|Implementation of Additional Real-Time Graphs and Diagnostic Displays|60|
| |Watch-Position Testing|5|
| |Trace Display|5|
| |Rate and Amplitude Stability Over Time|5|
| |Multi-Position Sequence Display|5|
| |Beat-Noise Scope Display|5|
| |Beat Error Display and Diagnostic Trace|5|
| |Long-Term Performance Graph|5|
| |Escapement Analyzer and Marker-Line Display|5|
| |Time-Frequency Spectrogram Display|5|
| |Waveform Comparison Display with Timing Markers|5|
| |Scope Mode with Synchronized Sweep Display|5|
| |Scope Function with Multiple Filter Views|5|
| |Grader Note: Each required graph or display should be demonstrated during the final presentation. Full points: feature is implemented, functional, and meaningfully integrated into the GUI Partial points: feature is partially implemented, incomplete, unreliable, or poorly integrated Zero points: feature is missing or non-functional| |
| | | |
|2|System Enhancements & AI Feature|25|
| |Sound Print enhancements improve event detection, readability, or interpretation|8|
| |Rate/Scope enhancements improve usefulness, accuracy, navigation, or measurement clarity|8|
| |Team-selected AI feature is implemented as a useful proof of concept|5|
| |Team explains what problem the AI feature addresses and how well it works|4|
| |Grader Note: Evaluate improvements to baseline graph views and AI-related enhancement. Use the following grading scale: Excellent - Enhancment is clearly improved, fully functional, and meaningfully more useful than the baseline. Enhancements improve event visibility, interpretation, or measurement quality, and the team explains the value of the changes. Strong - Enhancment has clear and useful improvements, with only minor limitations in completeness, polish, or robustness. Moderate - Some improvements are present, but they are limited in scope, incomplete, or only somewhat useful. Minimal - A small change was made, but it provides little practical improvement or is not clearly demonstrated. None / Missing - No meaningful Sound Print enhancement was implemented or demonstrated.| |
| | | |
|3|Quality Attribute Tradeoff Discussion (Shown in presentation slides and demonstration)|20|
| |Clearly identifies the major quality attributes relevant to the project|5|
| |Clearly explains tradeoffs among quality attributes|5|
| |Shows that accuracy was treated as the highest-priority attribute in the architecture and implementation<br><br>|5|
| |Explains what was actually achieved and what limitations remain|5|


| |Grader Note: Evaluate the team’s understanding of architectural tradeoffs, especially prioritizing accuracy. Use the following grading scale: Excellent - Clear, thoughtful, specific, and well supported by the team’s design and implementation choices Strong - Good explanation with minor gaps, limited detail, or weak support in one area Moderate - Partially correct or somewhat general explanation; shows some understanding but lacks depth or specificity Minimal - Very limited, vague, or weak explanation; little evidence of careful reasoning None / Missing - Not addressed or not meaningfully discussed| |
|---|---|---|
| | | |
|4|Performance, Latency, and Correctness (Shown in presentation slides and demonstration)|25|
| |Demonstrates real-time performance on the Raspberry Pi|8|
| |Demonstrates low latency from signal capture to display/update|6|
| |Demonstrates correctness of calculations, event detection, and displayed values|6|
| |Presents evidence, measurements, experiments, or observations supporting these claims|5|
| |Grader Note: Evaluate evidence that the architecture and implementation support runtime quality attributes. Real-time performance Full points: team clearly shows the system operating in real time on the target platform Partial points: some real-time behavior is shown, but performance is inconsistent, incomplete, or not demonstrated on target hardware No points: real-time performance is not meaningfully demonstrated Low latency Full points: team clearly demonstrates low latency between capture, processing, and display Partial points: latency is discussed or partially shown, but not well measured or not clearly acceptable No points: latency is not demonstrated or is clearly poor Correctness Full points: team shows that measurements, event detection, and displayed values are correct and consistent Partial points: correctness is only partly demonstrated, or some outputs appear questionable or inconsistent No points: correctness is not demonstrated or the outputs are clearly unreliable Supporting evidence Full points: team provides meaningful evidence such as experiments, measurements, logs, comparisons, or demonstrations Partial points: some evidence is shown, but it is weak, incomplete, or not well connected to the claims No points: little or no supporting evidence is presented| |
| | | |
|5|Extensibility of the Architecture (Shown in presentation slides)|20|
| |Architecture is modular and separates major concerns clearly|6|
| |Architecture supports adding new measurements, filters, graphs, or displays with limited redesign|6|
| |Team explains how the structure supports future requirements or enhancements|4|
| |Code organization and interfaces make the system understandable and maintainable|4|
| |Grader Note: Evaluate how well the architecture supports future change. Full points — clearly demonstrated and achieved Partial points — somewhat demonstrated, but missing clarity, flexibility, or completeness No points — not demonstrated, missing, or poorly supported| |
| | | |
|6|Remote User Interface / GUI Modifications|25|
| |GUI improvements make the system easier to use and understand|4|
| |System detects and responds appropriately to sensor or microphone unplug/replug events|5|


| |UI layout uses screen space effectively, including moving less-used controls into drop-downs or similar mechanisms|4|
|---|---|---|
| |GUI supports beat-synchronized display of A and C events, placing them in the same relative graph locations for each cycle so that deviations and irregular timing can be visually identified|4|
| |GUI reduces or filters handling noise, such as tapping on the watch or sensor, while preserving useful signal features such as A and C|4|
| |GUI provides clear overall system health, status, or measurement-readiness feedback|4|
| |Grader Note: Evaluate the usefulness and quality of changes to the GUI and user interaction model. Full points — clearly demonstrated and achieved Most points — demonstrated with minor limitations Partial points — somewhat demonstrated, but missing important functionality No points — not demonstrated, missing, or non-functional| |
| | | |
|7|Use of AI in Building the Software (Shown in presentation slides)|15|
| |Team clearly explains how AI tools were used in development|5|
| |Team shows thoughtful use of AI for design, coding, debugging, testing, documentation, or analysis|5|
| |Team reflects on the strengths, limitations, and risks of AI-assisted development|5|
| |Grader Note: Evaluate how the team used AI during development, not just whether they mention it. Full points — clearly demonstrated and thoughtfully explained Partial points — somewhat demonstrated, but missing depth, clarity, or meaningful reflection No points — not demonstrated, missing, or only mentioned superficially| |
| | | |
|8|Best user interface (UI)|10|
| |1st Best UI: 10 points; 2nd Best UI: 8 points, 3rd Best UI: 6 points, 4th Best UI: 4 points, 5th Best UI: 4 points(points awarded based on client/sponsor judgment)|10|
| |Grader Note: This area is based on comparative sponsor judgment.| |
| | | |
| |Total|200|
| | | |
| |Bonus Area. Additional Advanced Features — up to 15 bonus points|15|
| |Radar chart using measurements from multiple watch positions to help assess overall watch health|8|
| |Diagnosis/classification feature based on the readings|7|
| |Grader Note: Radar Chart 8 points: radar chart is implemented, uses meaningful multi-position watch data, and helps interpret overall watch health clearly 4–6 points: radar chart is implemented, but the presentation, usefulness, or integration is limited 1–3 points: radar chart is only partially implemented or only weakly connected to meaningful measurements 0 points: no meaningful radar chart feature is demonstrated<br><br>Diagnosis / Classification 7 points: diagnosis/classification feature is implemented and uses the measured data in a meaningful way to suggest watch condition, likely issues, or health interpretation 4–5 points: feature is implemented and somewhat useful, but limited in accuracy, explanation, or practical value 1–3 points: feature is only partially implemented or weakly tied to the actual readings 0 points: no meaningful diagnosis/classification feature is demonstrated| |


