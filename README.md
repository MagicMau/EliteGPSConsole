# EliteGPSConsole
An attempt to create a GPS system for Elite Dangerous by reading the coordinates from a screenshot. 

Fun with AForge and OCR. Uses the Windows 10 built in OCR engine.


OK, so I got lost not too long ago. I was playing Elite Dangerous, looking for the ancient ruins that had been found, but 
had a hard time finding them. I had the coordinates that I wanted to go to, but kept having to do the math in my head: do I take
a more northernly route here? Or perhaps go East?

That's when I figured reading the coordinates of the screen and doing some math on them would help and the idea to create
a GPS system was born.

This program uses AForge.NET for image manipulation and basic shape detection. It also uses the Windows 10 built in OCR engine, 
because it's quite good and much faster than for instance Tesseract. At least in my experience building this.

The idea is simple:
1. Take a screenshot
2. Run shape detection to find the altimeter, since just below that the coordinates are shown in the HUD
3. Extract a part of the screenshot where the coordinates should be
4. Filter out all background and keep only the numbers
5. Run OCR on the result to get the coordinates.

So that works. Sort of. No doubt it will take lots of tinkering to get it just right. 
At the moment I am only testing on the default color scheme at my monitor's resolution (2560x1440) at my FOV (70), and I am
pretty sure changing any of these parameters will mean more tinkering to get it to work again.

Anyway, here it is. Let's experiment!

License: do with this what you want, however, if you improve upon this, send a Pull Request so that I have your improvements too.
