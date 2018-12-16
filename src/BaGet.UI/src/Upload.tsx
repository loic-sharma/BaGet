import * as React from 'react';

class Upload extends React.Component {

  public render() {
    return (
      <div>
        <div>You can push a package using this command:</div>
        <div>dotnet nuget push -s http://localhost:5000/v3/index.json newtonsoft.json.11.0.2.nupkg</div>
      </div>
    );
  }
}

export default Upload;
