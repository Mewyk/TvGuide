### Notice
> This repository is still a work in progress with many features and code improvements to come. More information to come in the readme soon!

# TvGuide
Discord bot written in C# (.NET 9) for automatically displaying and updating information about twitch streams based on added users.
> Original idea for the bot is from my beyond amazing friend, [DJ from the future - ben1am](https://www.twitch.tv/ben1am)!

### Desktop Screenshot - How it looks
> From the Discord server `Channel 01`.
<img src="Resources/Screenshots/Desktop.Primary.png" alt="First Preview" width="100%" height="auto" />

### (Mobile) Screenshot - Adding a user
> From the Discord server `Channel 01`.
<img src="https://github.com/user-attachments/assets/f0f5cfc9-ebea-420a-a717-d4eafb542947" alt="Adding a user" width="25%" height="auto" />
<img src="https://github.com/user-attachments/assets/ec429818-3f6e-4192-a81b-de600bb4fef4" alt="User was added" width="25%" height="auto" />

## Notes
- The only required library outside of Microsoft and .NET libraries, is [NetCord](https://netcord.dev/) Discord C# library.
- The twitch portion of the code only requires an "app access token" and thus does not require any user specific tokens to access data.
