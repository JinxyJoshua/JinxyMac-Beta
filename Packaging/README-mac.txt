Jinxy AutoClicker — Mac
=======================

Runs on Apple silicon and Intel Macs, macOS 13 or later.
Nothing to install. The app carries everything it needs.


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
