#Collie - rounds all the files up into release

rm -rf bin/Release/DockingFuelPump
rm bin/Release/DockingFuelPump.zip

mkdir bin/Release/DockingFuelPump -p
mkdir bin/Release/DockingFuelPump/Plugins -p

cp bin/Release/DockingFuelPump.dll bin/Release/DockingFuelPump/Plugins/DockingFuelPump.dll

cp -a Assets/*.* bin/Release/DockingFuelPump/
cp LICENCE.txt bin/Release/DockingFuelPump/LICENCE.txt

#ruby -e "i=%x(cat Source/KerbalX.cs | grep version); i=i.split('=')[1].sub(';','').gsub('\"','').strip; s=\"echo 'version: #{i}' > bin/Release/KerbalX/version\"; system(s)"


rm bin/Release/DockingFuelPump.dll
rm bin/Release/DockingFuelPump.dll.mdb

cd bin/Release
zip -r DockingFuelPump.zip DockingFuelPump/


rm -rf /home/sujimichi/KSP/dev_KSP-1.3.1/GameData/DockingFuelPump/
cp -R DockingFuelPump/ /home/sujimichi/KSP/dev_KSP-1.3.1/GameData/DockingFuelPump/
