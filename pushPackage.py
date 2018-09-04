import os
import subprocess
import shutil

packagesPath = "/Users/ron/dev/python/content"
failedPushes = "/Users/ron/dev/python/failed"
uploadCount = 0
failedCount = 0
contentDir = os.listdir(packagesPath)

for package in contentDir:
    pushCommand = "dotnet nuget push %s -k ECB-NUGET-SERVER-KEY -s http://%s:80/v3/index.json" % (package, os.getenv('PRIMARY_IP_ADDRESS'))
    result = subprocess.call(['bash','-c', "cd %s && %s" % (packagesPath, pushCommand)]) 
    if result is not 0:
        try:
            shutil.move("%s/%s" % (packagesPath, package), "%s/%s" % (failedPushes, package))
            failedCount += 1
        except OSError as e:
            print ("Error: %s - %s." % (e.filename, e.strerror))
    else:    
        try:
            os.remove("%s/%s" % (packagesPath, package))
            uploadCount += 1 
        except OSError as e:
            print ("Error: %s - %s." % (e.filename, e.strerror))

print ("_______________________________________________")
print ("Summary:\n\nSuccessfuly uploaded: %d packages.\nFailed to upload: %d packages." % (uploadCount, failedCount))
print ("_______________________________________________")