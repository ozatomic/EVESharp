# Clone this repository
Of course you'll need a local copy of the emulator's code to be able to run it. You can download the most up to date version [here](https://github.com/Almamu/EVESharp/archive/master.zip)

Take in mind that this version might have regression bugs and other problems that "stable" releases do not have. Check the Releases and Tags section of the repository for more "stable" releases.

# Static data dump database files
Thankfully CCP provides official database dumps of their static data. This data has proven to be very useful for the emulator's development. Each release corresponds with different patch versions. The last compatible release with EVESharp can be downloaded from [here](https://files.evemu.dev/apoc/apo15-mysql5-v1.sql.bz2)

# EVE Online client
The EVESharp server is compatible with EVE Online Aprocrypha 6.14.101786 which can be downloaded from [here](https://files.evemu.dev/apoc/client/)

# BluePatcher
In order to be able to modify the game files to connect to any IP address the blue.dll file has to be patched to ignore the checksum checks done. There is an automatic patcher that automatically removes these checks. It can be downloaded from [here](https://files.evemu.dev/apoc/BlueAutoPatcher.exe)

# Database server installation
The server makes use of a MySQL database, this requires specific software to be installed. Depending on the OS you are goint to run the servers on there are different options.

It's important to have these files accessible as they will be required on the project's setup and configuration.

To perform the actual setup [go to the next step](Setup.md).