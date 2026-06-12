| | | |
|---|---|---|
|Area|Title and Scoring Criteria|Points Possible|
| | | |
|1|Mandatory Features|40|
| |Look up and display three additional aircraft information items other than route (e.g., Airline, Country, aircraft type)|5|
| |Look up and display route (e.g., line or graphical display)|5|
| |Look up and display aircraft point of origin and destination (e.g., JFK-PIT-LAX)|5|
| |Closest point of approach - Flight tracking of time, distance, altitude with nearby aircraft|10|
| |Plot airports|5|
| |Filter and show tracks based upon minimum of two polygon or areas only|10|
| | | |
|2|Responsiveness / Performance (Measured by selecting purge than monitor the desired action)|30|
| |Loading tracks from Raspberry Pi ADS-B Station < 1 sec|5|
| |Loading tracks from SBS < 1 sec|5|
| |Looking up aircraft data for selected track < 1 sec|5|
| |Looking up / calculate CPA < 1 sec|5|
| |Filter tracks based upon polygon < 1 sec|10|
| | | |
|3|Fault Detection and Error Handling (Turn off wifi or remove USB connection)|30|
| |Identify loss communication with Raspberry Pi|5|
| |Recover from loss communication with Raspberry Pi|5|
| |Identify the loss of incoming RP ADS-B local tracks|5|
| |Recover from loss of incoming RP ADS-B local tracks|5|
| |Gracefully handle missing data to support features (e.g., display N/A when missing information, conducted by selecting purge then observe data elements fill in) Hint - Use a playback file to show this capability|10|
| | | |
|4|Architecture supports new requirements or enhancements (Shown in presentation slides)|30|
| |Demonstrate adding or removing features to tailor functionality: 5 points minimum, up to 10 points (e.g., data analysis feature such as looking at all tracks heading west, how would you add this feature?, or how do you add more analysis to tracks within a polygon?)<br><br>|10|
| |Demonstrate that the remote user interface is capable of supporting the more computing intensive desired features: 5 points minimum, up to 10 points (e.g., how does the additional data scale?)<br><br>|10|
| |Demonstrate interfaces with other data sources: 5 points minimum, up to 10 points (e.g., how does the new data interface with remote user interface?)<br><br>|10|
| | | |
|5|Map Support|20|
| |Show zooming in and out to an area outside of Pittsburgh|8|
| |Show use of a different map data source (e.g., OpenStreet)|8|
| |Discuss why you chose this map data option|4|
| | | |
|6|Remote User Interface|40|
| |Real time display of maps and aircraft tracks (not just plotting tracks but projecting tracks when plots not received - show in the code)<br><br>|5|
| |Real time display of “hooked” aircraft<br><br>|5|


| |Display aircraft data and related metadata (e.g., show the difference between the original display and the new display)|5|
|---|---|---|
| |Implement click buttons (or similar feature) to remotely connect to the Raspberry Pi Flight Tracker, ADS-B Hub, recorded files, or other data files|5|
| |Implement slide bars or similar features to promote interaction of flight track data on the map visually (e.g., filter by altitude, speed, or aircraft type)|10|
| |Display polygons and show only aircraft that are within the defined waypoints on the map (e.g., square or irregular shape following an highway)<br><br>- Ease of use, user experience, display quality|10|
| | | |
|7|Best user interface (UI)|10|
| |1st Best UI: 10 points; 2nd Best UI: 8 points, 3rd Best UI: 6 points, 4th Best UI: 4 points, 5th Best UI: 4 points(points awarded based on client/sponsor judgment)|10|
| | | |
| | | |
| |Total|200|


Grading Scale

*Note: Lines 2-4 show max points combinations for each task and the scaling based upon the evaluation (outstanding, satisfactory, marginal, unsatisfactory, not acceptable).

|Outstanding|Satisfactory|Marginal|Unsatisfactory|Not Acceptable|
|---|---|---|---|---|
|10|8|5|3|0|
|8|6|4|2|0|
|5|4|3|1|0|
|4|3|2|1|0|
|Above and beyond Fully completed all aspects of the task. Demonstrated robust critical thinking through a comprehensive analysis that integrated relevant perspectives into an original, clear design rationale fully supported with consistent logic.|Completed all aspects of the task.<br><br>Demonstrated critical thinking through a comprehensive analysis that integrated relevant perspectives into a clear design rationale supported with consistent logic.|No change to existing code base, but executes the functional task or feature.|Feature does not meet performance specifications.<br><br>Feature does not meet the quality requirements.|Feature does not work.|
