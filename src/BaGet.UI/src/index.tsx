import { initializeIcons } from '@uifabric/icons';
import 'bootstrap/dist/css/bootstrap.css';
import { BrowserRouter as Router, Route } from 'react-router-dom';
import { config } from './config';
import * as React from 'react';
import * as ReactDOM from 'react-dom';
import App from './App';

import DisplayPackage from './DisplayPackage/DisplayPackage';
import Upload from './Upload';

import './index.css';

// import registerServiceWorker from './registerServiceWorker';

initializeIcons();

ReactDOM.render(
  <Router basename={config.baseUrl}>
    <App>
      <Route path="/packages/:id/:version?" component={DisplayPackage} />

      <Route path="/upload" component={Upload} />
    </App>
  </Router>,
  document.getElementById('root') as HTMLElement
);

// registerServiceWorker();
