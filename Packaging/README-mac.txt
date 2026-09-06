Jinxy AutoClicker — Mac
=======================

Runs on macOS 13 or later. Nothing to install; the app carries everything
it needs.

There are two downloads and they are not interchangeable:

  JinxyMac-mac.tar.gz          Apple silicon (M1, M2, M3, M4)
  JinxyMac-mac_intel.tar.gz    Intel

Apple menu › About This Mac tells you which you have. Before 1.2.3 there was a
single download holding both, and that is what stopped clicking working at all
— see the end of this file.


THE EASY WAY TO INSTALL
-----------------------

Open Terminal and paste this. It picks the right build for your Mac, installs
it, and opens it:

    curl -fsSL https://raw.githubusercontent.com/JinxyJoshua/JinxyMac-Beta/main/install.sh | sh

That is not just convenience. An app downloaded in a browser gets a quarantine
flag, and a quarantined app that is not signed by a paid Apple Developer
account is refused with a message saying it is damaged — see below. curl sets
no such flag, so an app fetched this way simply opens.

Everything under here is for anyone who would rather download it by hand.


OPENING IT THE FIRST TIME
-------------------------

macOS will refuse to open it and say the developer cannot be verified. That is
expected: the app is not signed by an Apple Developer account. It is not a
judgement about the app, only about who paid Apple $99 this year.

Two ways past it. Either works.

  The easy one
      Right-click (or Control-click) JinxyMac.app and choose Open.
      Click Open again in the dialog that appears.
      Only needed once. Double-clicking works from then on.

  The one-liner
      Open Terminal, type this, and press return:

          xattr -dr com.apple.quarantine /Applications/JinxyMac.app

      Adjust the path if you put the app somewhere other than Applications.


IF IT SAYS "DAMAGED AND CAN'T BE OPENED"
----------------------------------------

Click Cancel. Do not move it to the Trash — the download is not damaged and
that message is not about the file being broken.

It is what macOS says about a quarantined app whose bundle it cannot make sense
of, and unlike the message above, right-click and Open does NOT clear it. The
one-liner does:

    xattr -dr com.apple.quarantine /Applications/JinxyMac.app

Installing with the curl line at the top of this file avoids it entirely, which
is why that is the recommended way. The flag comes from the browser, not from
the app, and curl does not set it.

Drag JinxyMac.app to your Applications folder first if you want it to stay put.


TWO PERMISSIONS IT NEEDS
------------------------

macOS refuses both of these silently. Nothing crashes, nothing warns — the app
simply does nothing. The Settings page shows the state of each and re-checks on
demand, so you never have to guess which one is missing.

  Accessibility          Required to click at all.
                         System Settings > Privacy & Security > Accessibility

  Screen Recording       Required for the recorder and instant replay.
                         System Settings > Privacy & Security > Screen Recording

Add JinxyMac with the + button in each list. macOS usually asks you to quit and
reopen the app after granting Accessibility. It means it.

If you are updating from 1.2.2 or earlier, remove the old JinxyMac entry with
the minus button before adding this one. The app was replaced, so the old entry
points at something that is no longer there and macOS will not match it.


RECORDING NEEDS FFMPEG
----------------------

Clicking, hotkeys, presets, shake and history all work with nothing installed.
The recorder and instant replay need ffmpeg, which is free:

    brew install ffmpeg

If you do not have Homebrew, get it from https://brew.sh first. The app tells
you the same command if it cannot find ffmpeg, and everything else keeps
working without it.


WHERE THINGS GO
---------------

  Clips        ~/Movies/Jinxy Clips        (changeable in Settings)
  Settings     ~/Library/Application Support/JinxyMac

Deleting that second folder resets the app completely. There is also a Reset
button on the Settings page that does the same thing.


IF IT WILL NOT OPEN AT ALL
--------------------------

Open Terminal and run the app directly. It will print the reason, which the
Finder swallows:

    /Applications/JinxyMac.app/Contents/MacOS/JinxyMac

The most common answers are the quarantine flag (see above) and a macOS older
than 13.


A NOTE ON WHAT THIS IS
----------------------

This is the Mac build of a Windows app, written by someone without a Mac. The
timing engine, presets and settings format are shared with the Windows version
and are well tested. The parts that talk to macOS specifically — clicking,
hotkeys, screen capture, the menu bar — could not be run before release.

If something misbehaves, the fault is far more likely to be in those parts than
anywhere else, and knowing exactly what you saw is genuinely useful.

That is not hypothetical. Every build before 1.2.3 shipped one download holding
both architectures, picked at startup by a small script. macOS works out which
app a running program belongs to from where its executable sits, and that script
left the real program one folder too deep — so as far as the system was
concerned the app had no identity at all. The visible symptom was the menu bar
reading "Avalonia Application" instead of Jinxy. The one that mattered was that
Accessibility could never be granted: the permission is given to JinxyMac.app,
and the process asking for it was not recognised as being JinxyMac.app. Ticking
the box did nothing, every time, for everybody, since 1.0.7. It is fixed here by
shipping one proper app per architecture, which is why there are now two
downloads.
