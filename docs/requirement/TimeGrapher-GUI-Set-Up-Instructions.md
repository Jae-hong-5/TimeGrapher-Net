# TimeGrapher GUI Set Up Instructions

## Set Up on Computer

Note: This guide uses a Windows computer. The setup for a Macbook is similar.

### Download the software

Download the Solvit, Inc. TimeGrapher GUI code from the following link: https://canvas.cmu.edu/courses/53210/files/folder/Project?preview=14436250

For the LG Class, make sure you download or use the version named TimeGrapher_v10.5_Student.

Once the .zip file has downloaded, move or copy it into the folder where you want to keep the TimeGrapher files.

### Extract the zip file

Locate the downloaded .zip file. Right-click the file and select Extract All…. When the extraction wizard suggests a destination path, recommend removing the extra folder name TimeGrapher_v10.5_Student from the end of the path. For example, change:

C:\Users\YourName\Documents\TimeGrapher_v10.5_Student\TimeGrapher_v10.5_Student to: C:\Users\YourName\Documents\TimeGrapher_v10.5_Student

Click Extract.

### Download and install Qt Creator IDE

Go to the Qt Creator download page:

https://www.qt.io/development/tools/qt-creator-ide Click Download Qt and Qt Creator. Click Explore Qt Community Edition.

Select the operating system you will use to run Qt. Select the Qt Online Installer for your operating system. Depending on your operating system, one of the following installer files will download:

.dmg for macOS

.run for Linux

.exe for Windows Once the download is complete, run the downloaded installer file.

### Create and sign in with a Qt account

Run the downloaded Qt installer file. When prompted, create a Qt account or sign in with an existing Qt account. After creating a new Qt account, check your email inbox for a verification message from Qt. Click the verification link in the email to verify your account. Return to the installer and sign in using your Qt account credentials. Continue with the installation after your account has been verified. Follow the installation prompts to install Qt Creator IDE.

### Build the application

Open the main TimeGrapher project folder. Locate the file named: CMakeLists.txt Right-click CMakeLists.txt. Select Open with, then choose Choose another app. Select Qt Creator from the list of available applications. Choose Just once.

This prevents Windows from changing the default program for all .txt files to Qt Creator. Qt Creator will open the project and begin the setup process for building the application.

![image 1](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile1.png>)

![image 2](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile2.png>)

### Optional: Close notification messages

When Qt Creator opens, several small message boxes may appear in the lower-right corner of the Qt Creator window.

These messages usually provide status updates or optional actions. You may close them by clicking the X on each message.

![image 3](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile3.png>)

### Configure the project in Qt Creator

After opening CMakeLists.txt, Qt Creator will start in a project configuration screen. In the build configuration options, make sure Release is selected. Click Configure Project in the lower-right corner of the window. During this step, Qt Creator prepares the project files and build settings needed to compile the application.

![image 4](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile4.png>)

Wait a few minutes while Qt Creator configures the project.

### Build the release version

In Qt Creator, locate the build configuration selector in the lower-left corner of the window. Click the arrow or configuration menu that currently shows Debug.

![image 5](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile5.png>)

Select Release. Click anywhere outside the menu to close it.

![image 6](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile6.png>)

From the top menu bar, select Build to display the available build options. Select Run from the bottom of the Build menu. Qt Creator will build and run the application using the Release configuration.

![image 7](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile7.png>)

### Confirm the application runs

After selecting Run, Qt Creator will build and launch the application. This will take several minutes to complete.

If the TimeGrapher window shown below appears, congratulations! You have successfully installed Qt Creator, configured the project, built the release version, and launched the GUI application.

The display will switch from this display to the TimeGrapher GUI screen.

![image 8](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile8.png>)

## Set Up on Raspberry Pi

This section provides instructions for running the TimeGrapher GUI application on a Raspberry Pi.

The Raspberry Pi image already includes the following items:

- ● Qt Creator IDE executable
- ● TimeGrapher_v10.5_Student project files


After applying power to the Raspberry Pi and turning on the touchscreen, the Raspberry Pi desktop should appear.

Before running the TimeGrapher application, take a few minutes to customize any needed system settings, such as connecting to Wi-Fi or changing username and password. The default username is lg and the default password is lg (LG in lower case letters).

![image 9](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile9.png>)

### Prepare the Qt Creator installer

Open a Terminal window. Change to the Desktop directory: cd ~/Desktop Make the Qt online installer executable:

chmod 755 qt-online-installer-linux-arm64-4.11.0.run This command gives the installer permission to run on the Raspberry Pi.

![image 10](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile10.png>)

### Run the Qt online installer

After making the installer executable, locate the Qt online installer on the Desktop Double-click the installer file. When prompted, select Execute. During the installation process, sign in using your Qt account username and password. Follow the installer prompts to continue the Qt Creator installation.

### Extract the TimeGrapher project files

Locate the TimeGrapher_v10.5_Student.zip file. Right-click the .zip file. Select Extract Here. Wait for the extraction process to finish. After extraction is complete, a folder named TimeGrapher_v10.5_Student should appear in the same location as the .zip file.

![image 11](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile11.png>)

### Modify permissions for the extracted folder

Locate the extracted TimeGrapher_v10.5_Student folder. Right-click the folder and select Properties. Select the Permissions tab. Find the setting labeled Change content. If Change content is set to Nobody, change it to one of the following:

Only Owner or Anyone Click OK when finished.

![image 12](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile12.png>)

![image 13](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile13.png>)

### Install libasound2-dev

Open the extracted TimeGrapher_v10.5_Student folder. Locate and double-click the file named: LinuxAudio.cpp In the file, find the sudo install command on line 12.

sudo apt-get install libasound2-dev Copy the full sudo command. Open a Terminal window. Paste the copied command into the terminal and press Enter. If prompted, enter the Raspberry Pi password and press Enter again. This installs the Linux audio development library required to build the TimeGrapher application on the Raspberry Pi.

![image 14](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile14.png>)

![image 15](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile15.png>)

### Start Qt Creator

Open the extracted TimeGrapher_v10.5_Student folder. Locate the file named: CMakeLists.txt Right-click CMakeLists.txt. Select Open With…. Open the Programming drop-down menu. Select Qt Creator. Qt Creator will open the TimeGrapher project and prepare it for configuration and building on the Raspberry Pi.

![image 16](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile16.png>)

![image 17](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile17.png>)

### Configure and Build the release version

Configure and Build the Raspberry Pi version using the same process described in the previous Build the release version section.

In Qt Creator, make sure the project is configured for Release mode. Use the build configuration selector in the lower-left corner to switch from Debug to Release, if needed. From the top menu bar, select Build. Select Run. Qt Creator will build and launch the TimeGrapher GUI application on the Raspberry Pi.

### Enjoy the GUI application and explore the settings

After the TimeGrapher GUI application opens successfully, take a few minutes to explore the interface and available settings.

Congratulations! You have successfully installed, built, and launched the TimeGrapher GUI application on the Raspberry Pi.

![image 18](<TimeGrapher-GUI-Set-Up-Instructions_images/imageFile18.png>)
