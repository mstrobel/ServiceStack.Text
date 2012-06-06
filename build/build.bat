
REM SET BUILD=Debug
SET BUILD=Release

COPY ..\src\StrobelStack.Text\bin\%BUILD%\*.* ..\..\ServiceStack\release\latest\
COPY ..\src\StrobelStack.Text\bin\%BUILD%\*.* ..\..\ServiceStack\release\latest\StrobelStack.Text\
COPY ..\src\StrobelStack.Text\bin\%BUILD%\*.* ..\..\ServiceStack\lib
COPY ..\src\StrobelStack.Text\bin\%BUILD%\*.* ..\..\ServiceStack.Contrib\lib
COPY ..\src\StrobelStack.Text\bin\%BUILD%\*.* ..\..\ServiceStack.Redis\lib
COPY ..\src\StrobelStack.Text\bin\%BUILD%\*.* ..\..\ServiceStack.Examples\lib
COPY ..\src\StrobelStack.Text\bin\%BUILD%\*.* ..\..\ServiceStack.RedisWebServices\lib

COPY ..\src\StrobelStack.Text\bin\%BUILD%\*.* ..\NuGet\lib\net35
