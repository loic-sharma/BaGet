import * as React from 'react';

interface IRepoDetailsProps {
  url: string;
}

class RepoDetails extends React.Component<IRepoDetailsProps> {

  public render() {
    if (!this.props.url) {
      return null;
    }

    return (  
      <h2>Project Details</h2>
    );
  }
}

export default RepoDetails;

